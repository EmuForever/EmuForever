using Framework.Constants;
using Game.Entities;
using Game.Networking;
using Game.Networking.Packets;
using Game.Services.Classic;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    public partial class WorldSession
    {
        internal static readonly ConcurrentDictionary<uint, WorldSession> BnetSessions = new();
        readonly ConcurrentQueue<Action> _classicCompletions = new();
        readonly SemaphoreSlim _classicGate = new(1, 1);
        volatile bool _classicDisposed;
        bool _friendsSubscribed;
        bool _friendsRefreshing;
        DateTime _nextFriendRefresh;
        HashSet<uint> _bnetFriendIds = new();
        volatile BnetCharacter _bnetCharacterSnapshot;
        DateTime _nextBnetCharacterSnapshot;

        void BeginClassicService<T>(Func<Task<T>> work, Action<T> completed, Action<Exception> failed)
        {
            _ = Execute();
            async Task Execute()
            {
                await _classicGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_classicDisposed) return;
                    T result = await work().ConfigureAwait(false);
                    _classicCompletions.Enqueue(() => completed(result));
                }
                catch (Exception error) { _classicCompletions.Enqueue(() => failed(error)); }
                finally { _classicGate.Release(); }
            }
        }

        void ProcessClassicServices()
        {
            if (_classicDisposed) return;
            if (!IsServerControlled && DateTime.UtcNow >= _nextBnetCharacterSnapshot)
            {
                _nextBnetCharacterSnapshot = DateTime.UtcNow.AddSeconds(1);
                UpdateBnetCharacterSnapshot();
            }
            while (_classicCompletions.TryDequeue(out Action completion))
            {
                try { completion(); }
                catch (Exception error) { Log.outError(LogFilter.SessionRpc, $"Classic service completion: {error}"); }
            }
            if (_friendsSubscribed && !_friendsRefreshing && DateTime.UtcNow >= _nextFriendRefresh)
            {
                _friendsRefreshing = true;
                _nextFriendRefresh = DateTime.UtcNow.AddSeconds(25 + Random.Shared.Next(11));
                var previous = new HashSet<uint>(_bnetFriendIds);
                BeginClassicService(async () =>
                {
                    uint me = (uint)GetBattlenetAccountId();
                    // GetFriends batches the online-account query instead of querying each friend.
                    BnetCall call = await WorldFriends.Handle(ServiceConfiguration.Current, me, 5, System.Array.Empty<byte>());
                    if (call.Status != 0) throw new InvalidOperationException($"Friends refresh returned 0x{call.Status:X}");
                    foreach (var row in ProtoField.Walk(call.Body))
                    {
                        if (row.Field != 1 || row.Wire != 2) continue;
                        uint id = 0;
                        foreach (var field in ProtoField.Walk(row.Bytes))
                            if (field.Field == 1 && field.Wire == 0) id = (uint)field.Varint;
                        if (id == 0 || previous.Contains(id)) continue;
                        var note = new ProtoWriter(); note.Varint(1, me); note.Bytes(2, row.Bytes);
                        call.Notes.Add(new BnetNote { AccountId = me, Service = WorldFriends.ListenerHash, Method = 0xC0000001, Body = note.ToArray() });
                    }
                    return call;
                }, result =>
                {
                    foreach (uint id in new List<uint>(_bnetFriendIds))
                        if (result.FriendIds == null || !result.FriendIds.Contains(id)) SendBnetNote(WorldFriends.RemovedFriendNotification((uint)GetBattlenetAccountId(), id));
                    foreach (BnetNote note in result.Notes)
                        if (note.Service != WorldFriends.ListenerHash || !_bnetFriendIds.Contains(ReadFriendId(note.Body))) SendBnetNote(note);
                    _bnetFriendIds = result.FriendIds ?? new HashSet<uint>();
                    _friendsRefreshing = false;
                }, error =>
                {
                    _friendsRefreshing = false;
                    Log.outError(LogFilter.SessionRpc, $"Battle.net friend refresh: {error.Message}");
                });
            }
        }

        static uint ReadFriendId(byte[] body)
        {
            foreach (var outer in ProtoField.Walk(body))
                if (outer.Field == 2 && outer.Wire == 2)
                    foreach (var inner in ProtoField.Walk(outer.Bytes))
                        if (inner.Field == 1 && inner.Wire == 0) return (uint)inner.Varint;
            return 0;
        }

        void InitializeBnetServices()
        {
            if (IsServerControlled || GetBattlenetAccountId() == 0) return;
            BnetSessions[(uint)GetBattlenetAccountId()] = this;
            SendPacket(new ConnectionStatus { State = 1, SuppressNotification = false });
            SendClassicBnetResponse(new MethodCall { Type = ((ulong)WorldFriends.ServiceHash << 32) | 0x40000001u, ObjectId = 1 }, 0, System.Array.Empty<byte>());
            SendClassicBnetResponse(new MethodCall { Type = ((ulong)0x8E8F5FB0 << 32) | 0x40000001u, ObjectId = 1 }, 0, new byte[] { 0x0A, 0x00 });
        }

        void DisposeClassicServices()
        {
            _classicDisposed = true;
            if (BnetSessions.TryGetValue((uint)GetBattlenetAccountId(), out var current) && current == this)
                BnetSessions.TryRemove((uint)GetBattlenetAccountId(), out _);
            while (_classicCompletions.TryDequeue(out _)) { }
        }

        internal bool TryBnetCharacter(out BnetCharacter character)
        {
            character = _bnetCharacterSnapshot;
            return character != null;
        }

        void UpdateBnetCharacterSnapshot()
        {
            Player player = GetPlayer();
            if (player == null || !player.IsInWorld) { _bnetCharacterSnapshot = null; return; }
            var character = new BnetCharacter();
            character.GameAccountId = (uint)GetAccountId();
            character.AccountName = GetAccountName();
            character.Name = player.GetName();
            character.Race = (byte)player.GetRace();
            character.Class = (byte)player.GetClass();
            character.Gender = (byte)player.GetNativeGender();
            character.Level = (byte)player.GetLevel();
            character.Zone = (uint)player.GetZoneId();
            character.GuidLow = (uint)player.GetGUID().GetCounter();
            character.RealmAddress = ServiceConfiguration.Current.RealmAddress;
            character.RealmId = ServiceConfiguration.Current.RealmID;
            _bnetCharacterSnapshot = character;
        }

        bool HandleClassicBnetRequest(BattlenetRequest request)
        {
            uint hash = request.Method.GetServiceHash();
            uint method = request.Method.GetMethodId() & 0x3FFFFFFF;
            if (hash == ClassicCharacterScreenUtilities.ServiceHash)
            {
                BeginClassicService(() => ClassicCharacterScreenUtilities.Handle(method, request.Data), result =>
                {
                    Log.outInfo(LogFilter.SessionRpc, $"Character-screen utility {result.Command}: status 0x{result.Status:X} for account {GetAccountId()}");
                    SendClassicBnetResponse(request.Method, result.Status, result.Body);
                }, error =>
                {
                    Log.outError(LogFilter.SessionRpc, $"Character-screen utility failed for account {GetAccountId()}: {error.Message}");
                    SendClassicBnetResponse(request.Method, (uint)BattlenetRpcErrorCode.UtilServerFailedToSerializeResponse, System.Array.Empty<byte>());
                });
                return true;
            }
            if (hash == 0x94B94786 || hash == 0xE273DE0E)
            {
                BnetCall result = ClassicSocialSupport.Handle(hash, method, _player != null, (ulong)(_player?.GetGuildId() ?? 0), request.Data);
                SendClassicBnetResponse(request.Method, result.Status, result.Body);
                return true;
            }
            if (hash == 0x138D200C)
            {
                SendClassicBnetResponse(request.Method, 0, System.Array.Empty<byte>());
                return true;
            }
            if (hash == 0x8E8F5FB0)
            {
                HandleBnetBlockList(request.Method, method, request.Data);
                return true;
            }
            if (hash != WorldFriends.ServiceHash) return false;
            BeginClassicService(() => WorldFriends.Handle(ServiceConfiguration.Current, (uint)GetBattlenetAccountId(), method, request.Data), call =>
            {
                SendClassicBnetResponse(request.Method, call.Status, call.Body);
                if (call.Status != 0) return;
                if (call.FriendIds != null) _bnetFriendIds = call.FriendIds;
                if (method == 1 || method == 5) _friendsSubscribed = true;
                if (method == 2) _friendsSubscribed = false;
                foreach (var note in call.Notes)
                    if (BnetSessions.TryGetValue(note.AccountId, out var target)) target._classicCompletions.Enqueue(() => target.SendBnetNote(note));
            }, error =>
            {
                Log.outError(LogFilter.SessionRpc, $"Battle.net friends method {method}: {error}");
                SendClassicBnetResponse(request.Method, 1, System.Array.Empty<byte>());
            });
            return true;
        }

        void SendClassicBnetResponse(MethodCall method, uint status, byte[] payload)
        {
            method.ObjectId = 1;
            var response = new Response { Method = method, BnetStatus = (BattlenetRpcErrorCode)status };
            response.Data.WriteBytes(payload);
            SendPacket(response);
        }

        void HandleBnetBlockList(MethodCall call, uint method, byte[] payload)
        {
            if (method == 2) { SendClassicBnetResponse(call, 0, System.Array.Empty<byte>()); return; }
            if (method != 1 && method != 3) { SendClassicBnetResponse(call, 0xBC3, System.Array.Empty<byte>()); return; }
            BeginClassicService(async () =>
            {
                var state = new ProtoWriter();
                await using var connection = Framework.Database.DB.Login.CreateConnection();
                await connection.OpenAsync();
                await using var command = new MySqlConnector.MySqlCommand("SELECT b.blocked_account_id,COALESCE(a.battle_tag,'') FROM bnot_blocks b JOIN bnot_accounts a ON a.id=b.blocked_account_id WHERE b.account_id=@id", connection);
                command.Parameters.AddWithValue("@id", GetBattlenetAccountId());
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var player = new ProtoWriter(); player.Varint(1, reader.GetUInt32(0)); player.String(2, reader.GetString(1));
                    state.Message(1, player);
                }
                var response = new ProtoWriter(); response.Message(1, state); return response.ToArray();
            }, body => SendClassicBnetResponse(call, 0, body), error =>
            {
                Log.outError(LogFilter.SessionRpc, $"Battle.net block list: {error.Message}");
                SendClassicBnetResponse(call, 1, System.Array.Empty<byte>());
            });
        }

        void SendBnetNote(BnetNote note)
        {
            if (WorldFriends.IsFriendListNotification(note))
            {
                uint friendId = ReadFriendId(note.Body);
                if (friendId != 0)
                {
                    if (note.Method == 0xC0000001) _bnetFriendIds.Add(friendId);
                    else _bnetFriendIds.Remove(friendId);
                }
            }
            var notification = new Notification { Method = new MethodCall { Type = ((ulong)note.Service << 32) | note.Method, ObjectId = 1 } };
            notification.Data.WriteBytes(note.Body);
            SendPacket(notification);
        }
    }

    internal sealed class ClassicServiceResponse : ServerPacket
    {
        readonly byte[] _body;
        public ClassicServiceResponse(uint opcode, byte[] body) : base((ServerOpcodes)opcode, ConnectionType.Realm) { _body = body; }
        public override void Write() => _worldPacket.WriteBytes(_body);
    }

    internal sealed class ClassicShopRequest : ClientPacket
    {
        public ClassicShopRequest(WorldPacket packet) : base(packet) { }
        public byte[] Payload;
        public override void Read() => Payload = _worldPacket.GetData();
    }
}
