using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Networking;
using Game.Services.Classic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    public partial class WorldSession
    {
        ShopPendingPurchase _shopPending;
        static long _shopNextId = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() << 16;

        sealed class ShopReply
        {
            public readonly List<(uint Opcode, byte[] Body)> Packets = new();
            public ShopPendingPurchase Delivered;
            public bool RefreshCharacters;
            public void Add(uint opcode, byte[] body) => Packets.Add((opcode, body));
        }

        [WorldPacketHandler(ClientOpcodes.BattlePayGetProductList, Status = SessionStatus.Authed)]
        [WorldPacketHandler(ClientOpcodes.BattlePayGetPurchaseList, Status = SessionStatus.Authed)]
        [WorldPacketHandler(ClientOpcodes.BattlePayStartPurchase, Status = SessionStatus.Authed)]
        [WorldPacketHandler(ClientOpcodes.BattlePayConfirmPurchaseResponse, Status = SessionStatus.Authed)]
        [WorldPacketHandler(ClientOpcodes.BattlePayAckFailedResponse, Status = SessionStatus.Authed)]
        [WorldPacketHandler(ClientOpcodes.BattlePayDistributionAssignToTarget, Status = SessionStatus.Authed)]
        [WorldPacketHandler(ClientOpcodes.GetVasAccountCharacterList, Status = SessionStatus.Authed)]
        [WorldPacketHandler(ClientOpcodes.GetVasTransferTargetRealmList, Status = SessionStatus.Authed)]
        [WorldPacketHandler(ClientOpcodes.BattlePayStartVasPurchase, Status = SessionStatus.Authed)]
        void HandleClassicShop(ClassicShopRequest request)
        {
            uint opcode = (uint)request.GetOpcode();
            BeginClassicService(() => ProcessShopRequest(opcode, request.Payload), reply =>
            {
                if (reply.Delivered != null)
                {
                    try { ApplyShopDelivery(reply.Delivered); }
                    catch (Exception error) { Log.outError(LogFilter.Player, $"Shop purchase {reply.Delivered.Id} committed; live notification failed: {error}"); }
                }
                foreach (var packet in reply.Packets) SendPacket(new ClassicServiceResponse(packet.Opcode, packet.Body));
                if (reply.RefreshCharacters) HandleCharEnum(new Networking.Packets.EnumCharacters(new WorldPacket()));
            }, error =>
            {
                Log.outError(LogFilter.SessionRpc, $"Shop request 0x{opcode:X}: {error}");
                // Even database/validation failures must release the client's pending request.
                if (opcode == (uint)ClientOpcodes.BattlePayGetProductList)
                    SendPacket(new ClassicServiceResponse(BattlePayPackets.ProductListOpcode, BattlePayPackets.ProductList(new ShopCatalog(System.Array.Empty<ShopGroup>(), System.Array.Empty<ShopProduct>()), false)));
                else if (opcode == (uint)ClientOpcodes.BattlePayGetPurchaseList)
                    SendPacket(new ClassicServiceResponse(BattlePayPackets.PurchaseListOpcode, BattlePayPackets.PurchaseList(System.Array.Empty<ShopPurchase>(), false)));
            });
        }

        async Task<ShopReply> ProcessShopRequest(uint opcode, byte[] payload)
        {
            var reply = new ShopReply();
            var cfg = ServiceConfiguration.Current;
            uint account = (uint)GetAccountId();
            ServiceBuffer data = BattlePayPackets.Request(payload);
            ShopPendingPurchase previousPending = _shopPending;
            try
            {
            switch ((ClientOpcodes)opcode)
            {
                case ClientOpcodes.BattlePayGetProductList:
                    BattlePayPackets.End(data);
                    reply.Add(BattlePayPackets.ProductListOpcode, BattlePayPackets.ProductList(cfg.ShopEnabled ? await ShopDb.Catalog(cfg) : new([], []), cfg.ShopEnabled));
                    reply.Add(BattlePayPackets.DistributionListOpcode, BattlePayPackets.DistributionList(cfg.ShopEnabled ? await ShopDb.Distributions(cfg, account) : [], cfg.ShopEnabled));
                    break;
                case ClientOpcodes.BattlePayGetPurchaseList:
                    BattlePayPackets.End(data);
                    reply.Add(BattlePayPackets.PurchaseListOpcode, BattlePayPackets.PurchaseList(cfg.ShopEnabled ? await ShopDb.Purchases(cfg, account) : [], cfg.ShopEnabled));
                    break;
                case ClientOpcodes.BattlePayStartPurchase:
                    await StartShopPurchase(BattlePayPackets.ReadStart(data), reply);
                    break;
                case ClientOpcodes.BattlePayConfirmPurchaseResponse:
                    await ConfirmShopPurchase(BattlePayPackets.ReadConfirm(data), reply);
                    break;
                case ClientOpcodes.BattlePayAckFailedResponse:
                    _shopPending = null;
                    break;
                case ClientOpcodes.GetVasAccountCharacterList:
                case ClientOpcodes.GetVasTransferTargetRealmList:
                {
                    uint token = data.ReadUInt32();
                    int kind = data.ReadInt32();
                    BattlePayPackets.End(data);
                    bool characters = opcode == (uint)ClientOpcodes.GetVasAccountCharacterList;
                    var rows = characters && cfg.ShopEnabled ? await GameDb.LoadCharacters(cfg, account) : [];
                    var body = new ServiceBuffer();
                    body.WriteUInt32(token); body.WriteInt32(cfg.ShopEnabled ? 0 : 1); body.WriteInt32(kind); body.WriteUInt32((uint)rows.Count);
                    foreach (var row in rows)
                    {
                        ServiceBuffer.WriteGuid(body, 29ul << 58, account);
                        ServiceBuffer.WriteGuid(body, PlayerHigh, row.Guid);
                        body.WriteUInt32(cfg.RealmAddress);
                        body.WriteUInt8(row.Race); body.WriteUInt8(row.Class); body.WriteUInt8(row.Gender); body.WriteUInt8(row.Level);
                        body.WriteInt64(0); body.WriteInt32(0);
                        BattlePayPackets.StringSize(body, row.Name, 6); BattlePayPackets.StringSize(body, cfg.RealmName, 9);
                        body.WriteBits(0, 9); body.FlushBits(); body.WriteSizedString(row.Name); body.WriteSizedString(cfg.RealmName);
                    }
                    reply.Add(characters ? 0x450299u : 0x45029Au, body.ToArray());
                    break;
                }
                case ClientOpcodes.BattlePayStartVasPurchase:
                {
                    uint token = data.ReadUInt32(), product = data.ReadUInt32();
                    var target = ServiceBuffer.ReadGuid(data);
                    uint sourceRealm = data.ReadUInt32(), destination = data.ReadUInt32();
                    for (int i = 0; i < 3; ++i)
                    {
                        var extra = ServiceBuffer.ReadGuid(data);
                        if (extra.High != 0 || extra.Low != 0) throw new InvalidDataException("Cross-account services are unavailable");
                    }
                    int[] lengths = { (int)data.ReadBits(6), (int)data.ReadBits(7), (int)data.ReadBits(7), (int)data.ReadBits(6), (int)data.ReadBits(12) };
                    data.ReadBit(); data.ResetBitPos();
                    foreach (int length in lengths) data.ReadString(length);
                    BattlePayPackets.End(data);
                    if ((sourceRealm != 0 && sourceRealm != cfg.RealmAddress) || (destination != 0 && destination != cfg.RealmAddress))
                        reply.Add(BattlePayPackets.StartResponseOpcode, BattlePayPackets.StartResponse(token, 0, 1));
                    else
                        await StartShopPurchase(new(token, product, target.High, target.Low, 0, "", "", "", true), reply);
                    break;
                }
                case ClientOpcodes.BattlePayDistributionAssignToTarget:
                {
                    var request = BattlePayPackets.ReadAssign(data);
                    bool valid = cfg.ShopEnabled && request.TargetHigh == PlayerHigh && Global.ObjAccessor.FindPlayer(ObjectGuid.Create(HighGuid.Player, (long)request.TargetLow)) == null
                        && await ShopDb.Owns(cfg, account, request.TargetLow);
                    bool assigned = valid && await ShopBoostService.Assign(cfg, account, request);
                    reply.Add(0x450231, BattlePayPackets.AssignResponse(request.ClientToken, request.DistributionId, assigned ? 0 : 1));
                    if (assigned)
                    {
                        var character = new ServiceBuffer(); ServiceBuffer.WriteGuid(character, request.TargetHigh, request.TargetLow);
                        reply.Add(0x450269, character.ToArray()); reply.Add(0x45026A, character.ToArray());
                        reply.Add(BattlePayPackets.DistributionListOpcode, BattlePayPackets.DistributionList(await ShopDb.Distributions(cfg, account), true));
                        reply.RefreshCharacters = true;
                    }
                    break;
                }
            }
            }
            catch (Exception error)
            {
                Log.outError(LogFilter.SessionRpc, $"Shop request 0x{opcode:X}: {error}");
                ServiceBuffer retry = BattlePayPackets.Request(payload);
                uint token = retry.Remaining >= 4 ? retry.ReadUInt32() : 0;
                switch ((ClientOpcodes)opcode)
                {
                    case ClientOpcodes.BattlePayGetProductList:
                        if (!reply.Packets.Any(packet => packet.Opcode == BattlePayPackets.ProductListOpcode))
                            reply.Add(BattlePayPackets.ProductListOpcode, BattlePayPackets.ProductList(new([], []), false));
                        break;
                    case ClientOpcodes.BattlePayGetPurchaseList:
                        reply.Add(BattlePayPackets.PurchaseListOpcode, BattlePayPackets.PurchaseList([], false));
                        break;
                    case ClientOpcodes.BattlePayStartPurchase:
                    case ClientOpcodes.BattlePayStartVasPurchase:
                        _shopPending = null;
                        reply.Add(BattlePayPackets.StartResponseOpcode, BattlePayPackets.StartResponse(token, 0, 1));
                        break;
                    case ClientOpcodes.BattlePayConfirmPurchaseResponse:
                        if (previousPending != null && !reply.Packets.Any(packet => packet.Opcode == BattlePayPackets.PurchaseUpdateOpcode))
                        {
                            _shopPending = null;
                            await ShopPurchaseUpdate(reply, previousPending, 6, reply.Delivered != null ? 0 : 1);
                        }
                        break;
                    case ClientOpcodes.BattlePayDistributionAssignToTarget:
                        if (!reply.Packets.Any(packet => packet.Opcode == 0x450231))
                            reply.Add(0x450231, BattlePayPackets.AssignResponse(token, retry.Remaining >= 8 ? retry.ReadUInt64() : 0, 1));
                        break;
                    case ClientOpcodes.GetVasAccountCharacterList:
                    case ClientOpcodes.GetVasTransferTargetRealmList:
                        var empty = new ServiceBuffer(); empty.WriteUInt32(token); empty.WriteInt32(1); empty.WriteInt32(retry.Remaining >= 4 ? retry.ReadInt32() : 0); empty.WriteUInt32(0);
                        reply.Add(opcode == (uint)ClientOpcodes.GetVasAccountCharacterList ? 0x450299u : 0x45029Au, empty.ToArray());
                        break;
                }
            }
            return reply;
        }

        ulong PlayerHigh => (ulong)ObjectGuid.Create(HighGuid.Player, 0).GetHighValue();

        string ValidateShopProduct(ShopProduct product, ulong target)
        {
            foreach (var action in product.Actions)
            {
                if (!ShopDb.ValidAction(action)) return "Invalid delivery action";
                if (action.Kind == ShopActionKind.Item && !ItemStatCatalog.HasTemplate((uint)action.Param1)) return "Missing item";
                if (action.Kind is ShopActionKind.Spell or ShopActionKind.Level or ShopActionKind.Profession or ShopActionKind.Title or ShopActionKind.Reputation)
                {
                    if (_player == null || !_player.IsInWorld || (ulong)_player.GetGUID().GetCounter() != target || !_player.IsAlive() || _player.IsInCombat()) return "Log into the recipient outside combat";
                }
                if (action.Kind == ShopActionKind.Spell && Global.SpellMgr.GetSpellInfo(action.Param1, Difficulty.None) == null) return "Missing spell";
                if (action.Kind == ShopActionKind.Level && action.Param1 <= _player.GetLevel()) return "Level already reached";
                if (action.Kind == ShopActionKind.Title && !AchievementCatalog.TryGetTitleMask((uint)action.Param1, out _)) return "Missing title";
                if (action.Kind == ShopActionKind.Profession && CliDB.SkillLineStorage.LookupByKey((SkillType)action.Param1) == null) return "Missing skill";
                if (action.Kind == ShopActionKind.Reputation && CliDB.FactionStorage.LookupByKey(action.Param1) == null) return "Missing faction";
            }
            return null;
        }

        async Task StartShopPurchase(ShopStartRequest request, ShopReply reply)
        {
            _shopPending = null;
            var cfg = ServiceConfiguration.Current;
            try
            {
                var product = cfg.ShopEnabled ? (await ShopDb.Catalog(cfg)).Products.FirstOrDefault(p => p.Id == request.ProductId) : null;
                ulong target = request.TargetLow != 0 ? request.TargetLow : (ulong)(_player?.GetGUID().GetCounter() ?? 0);
                bool valid = product != null && (request.TargetLow == 0 || request.TargetHigh == PlayerHigh)
                    && await ShopDb.Owns(cfg, (uint)GetAccountId(), target)
                    && await ShopDb.Points(cfg, (uint)GetAccountId()) >= product.Price && ValidateShopProduct(product, target) == null;
                if (!valid) { reply.Add(BattlePayPackets.StartResponseOpcode, BattlePayPackets.StartResponse(request.ClientToken, 0, 1)); return; }
                var pending = new ShopPendingPurchase((ulong)Interlocked.Increment(ref _shopNextId), (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue), target,
                    product, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), Environment.TickCount64 + 120000, request.IsVas);
                _shopPending = pending;
                reply.Add(BattlePayPackets.StartResponseOpcode, BattlePayPackets.StartResponse(request.ClientToken, pending.Id, 0));
                await ShopPurchaseUpdate(reply, pending, 9, 0);
                reply.Add(BattlePayPackets.ConfirmOpcode, BattlePayPackets.Confirm(pending.Id, pending.ServerToken));
            }
            catch (Exception error)
            {
                Log.outError(LogFilter.SessionRpc, $"Shop start account {GetAccountId()}: {error.Message}");
                _shopPending = null;
                reply.Packets.Clear();
                reply.Add(BattlePayPackets.StartResponseOpcode, BattlePayPackets.StartResponse(request.ClientToken, 0, 1));
            }
        }

        async Task ConfirmShopPurchase(ShopConfirmRequest response, ShopReply reply)
        {
            var pending = _shopPending;
            if (pending == null || pending.ServerToken != response.ServerToken) return;
            _shopPending = null;
            var cfg = ServiceConfiguration.Current;
            bool committed = false;
            try
            {
                var current = response.Accepted && cfg.ShopEnabled ? (await ShopDb.Catalog(cfg)).Products.FirstOrDefault(p => p.Id == pending.Product.Id) : null;
                bool valid = current != null && current.Price == pending.Product.Price && current.Actions.SequenceEqual(pending.Product.Actions)
                    && Environment.TickCount64 <= pending.ExpiresAt && response.Price == BattlePayPackets.Price(pending.Product.Price)
                    && ValidateShopProduct(pending.Product, pending.Target) == null;
                committed = valid && await ShopDb.Commit(cfg, (uint)GetAccountId(), pending);
                if (committed)
                {
                    reply.Delivered = pending;
                    reply.Add(BattlePayPackets.DistributionListOpcode, BattlePayPackets.DistributionList(await ShopDb.Distributions(cfg, (uint)GetAccountId()), true));
                    if (pending.IsVas)
                    {
                        var row = (await GameDb.LoadCharacters(cfg, (uint)GetAccountId())).FirstOrDefault(c => c.Guid == pending.Target);
                        if (row != null)
                        {
                            var body = new ServiceBuffer(); body.WriteUInt32(pending.Product.Id); body.WriteUInt32(0);
                            ServiceBuffer.WriteGuid(body, PlayerHigh, pending.Target); ServiceBuffer.WriteEmptyGuid(body); body.WriteUInt32(cfg.RealmAddress); ServiceBuffer.WriteEmptyGuid(body);
                            BattlePayPackets.StringSize(body, row.Name, 6); BattlePayPackets.StringSize(body, cfg.RealmName, 9); body.FlushBits();
                            body.WriteSizedString(row.Name); body.WriteSizedString(cfg.RealmName); reply.Add(0x45029C, body.ToArray());
                        }
                        reply.RefreshCharacters = true;
                    }
                }
            }
            catch (Exception error) { Log.outError(LogFilter.SessionRpc, $"Shop confirmation {pending.Id}, committed={committed}: {error}"); }
            await ShopPurchaseUpdate(reply, pending, 6, committed ? 0 : 1);
        }

        async Task ShopPurchaseUpdate(ShopReply reply, ShopPendingPurchase pending, int status, int result)
        {
            uint points = 0;
            try { points = await ShopDb.Points(ServiceConfiguration.Current, (uint)GetAccountId()); }
            catch (Exception error) { Log.outError(LogFilter.SessionRpc, $"Shop points: {error.Message}"); }
            ulong price = BattlePayPackets.Price(pending.Product.Price);
            reply.Add(BattlePayPackets.PurchaseUpdateOpcode, BattlePayPackets.PurchaseUpdate(new(pending.Id, status, result, pending.Product.Id, price, price, pending.Created, $"Shop points: {points}")));
        }

        void ApplyShopDelivery(ShopPendingPurchase pending)
        {
            Player recipient = Global.ObjAccessor.FindPlayer(ObjectGuid.Create(HighGuid.Player, (long)pending.Target));
            if (recipient == null) return;
            foreach (var action in pending.Product.Actions)
            {
                switch (action.Kind)
                {
                    case ShopActionKind.Spell: recipient.SpellBook.Learn(action.Param1, false); break;
                    case ShopActionKind.Level: recipient.GiveLevel(action.Param1); break;
                    case ShopActionKind.Profession: int value = action.Param2 == 0 ? 300 : action.Param2; recipient.SetSkill((SkillType)action.Param1, 0, value, value); break;
                    case ShopActionKind.Title: recipient.SetTitle(CliDB.CharTitlesStorage.LookupByKey(action.Param1)); break;
                    case ShopActionKind.Reputation: recipient.GetReputationMgr().SetReputation(CliDB.FactionStorage.LookupByKey(action.Param1), action.Param2); break;
                    case ShopActionKind.Service: recipient.SetAtLoginFlag((AtLoginFlags)(action.Param1 switch { 1 => 1, 2 => 8, 3 => 128, 4 => 64, _ => 0 })); break;
                }
            }
            if (pending.Product.Actions.Any(action => action.Kind is ShopActionKind.Item or ShopActionKind.Gold))
                recipient.AddNewMailDeliverTime(LoopTime.ServerTime);
        }
    }
}
