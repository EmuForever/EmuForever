#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Framework.Constants;
using System.Text;

namespace Game.Services.Classic;

// Classic 1.60 layout from TrinityCore TokenPackets at 855d360d.
// Store prices use four decimal places, rather than copper or floating point.
static class BattlePayPackets
{
    public const uint ProductListOpcode = 0x45021C;
    public const uint PurchaseListOpcode = 0x45021D;
    public const uint DistributionListOpcode = 0x45021E;
    public const uint PurchaseUpdateOpcode = 0x450233;
    public const uint StartResponseOpcode = 0x450230;
    public const uint ConfirmOpcode = 0x450234;

    public static ulong Price(uint points) => (ulong)points * 10000;

    internal static void StringSize(ServiceBuffer data, string text, int bits)
    {
        int length = Encoding.UTF8.GetByteCount(text);
        if (length >= 1 << bits)
            throw new InvalidDataException($"Shop string exceeds {bits}-bit length");
        data.WriteBits((ulong)length, bits);
    }

    public static ServiceBuffer Request(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length < 4)
            throw new InvalidDataException("Missing client opcode");
        return ServiceBuffer.From(payload[4..]);
    }

    public static void End(ServiceBuffer data)
    {
        data.ResetBitPos();
        if (data.Remaining != 0)
            throw new InvalidDataException($"Unexpected trailing request data ({data.Remaining} bytes)");
    }

    public static ShopStartRequest ReadStart(ServiceBuffer data)
    {
        uint token = data.ReadUInt32();
        uint product = data.ReadUInt32();
        var target = ServiceBuffer.ReadGuid(data);
        uint unknown = data.ReadUInt32();
        int first = (int)data.ReadBits(6);
        int second = (int)data.ReadBits(12);
        int third = (int)data.ReadBits(7);
        data.ResetBitPos();
        string firstText = data.ReadString(first);
        string secondText = data.ReadString(second);
        string thirdText = data.ReadString(third);
        End(data);
        return new(token, product, target.High, target.Low, unknown,
            firstText, secondText, thirdText);
    }

    public static ShopConfirmRequest ReadConfirm(ServiceBuffer data)
    {
        uint token = data.ReadUInt32();
        ulong price = data.ReadUInt64();
        bool accepted = data.ReadBit();
        End(data);
        return new(token, price, accepted);
    }

    public static byte[] StartResponse(uint clientToken, ulong purchaseId, int result)
    {
        var data = new ServiceBuffer();
        data.WriteInt32(result);
        data.WriteUInt32(clientToken);
        data.WriteUInt64(purchaseId);
        return data.ToArray();
    }

    public static byte[] Confirm(ulong purchaseId, uint serverToken)
    {
        var data = new ServiceBuffer();
        data.WriteUInt64(purchaseId);
        data.WriteUInt32(serverToken);
        return data.ToArray();
    }

    public static byte[] PurchaseUpdate(ShopPurchase purchase)
    {
        var data = new ServiceBuffer();
        data.WriteUInt32(1);
        WritePurchase(data, purchase);
        return data.ToArray();
    }

    public static byte[] PurchaseList(IReadOnlyList<ShopPurchase> purchases, bool enabled)
    {
        var data = new ServiceBuffer();
        data.WriteInt32(enabled ? 0 : 2);
        data.WriteUInt32((uint)purchases.Count);
        foreach (ShopPurchase purchase in purchases)
            WritePurchase(data, purchase);
        return data.ToArray();
    }

    public static void WritePurchase(ServiceBuffer data, ShopPurchase purchase)
    {
        data.WriteUInt64(purchase.Id);
        data.WriteInt32(purchase.Status);
        data.WriteInt32(purchase.Result);
        data.WriteUInt32(purchase.ProductId);
        data.WriteUInt64(purchase.BasePrice);
        data.WriteUInt64(purchase.UserPrice);
        data.WriteInt64(purchase.Created);
        StringSize(data, purchase.Wallet, 8);
        data.FlushBits();
        data.WriteSizedString(purchase.Wallet);
    }

    public static ShopPurchase ReadPurchase(ServiceBuffer data)
    {
        ulong id = data.ReadUInt64();
        int status = data.ReadInt32();
        int result = data.ReadInt32();
        uint product = data.ReadUInt32();
        ulong normal = data.ReadUInt64();
        ulong current = data.ReadUInt64();
        long created = data.ReadInt64();
        int length = (int)data.ReadBits(8);
        data.ResetBitPos();
        return new(id, status, result, product, normal, current, created, data.ReadString(length));
    }

    public static byte[] ProductList(ShopCatalog catalog, bool enabled)
    {
        var data = new ServiceBuffer();
        ShopProduct[] products = enabled ? catalog.Products : [];
        ShopGroup[] groups = enabled ? catalog.Groups : [];
        var deliverables = new List<ShopDeliverable>();
        var productDeliverables = new Dictionary<uint, uint[]>();
        uint nextId = 0;
        foreach (ShopProduct product in products)
        {
            var ids = new List<uint>();
            foreach (ShopAction action in product.Actions)
            {
                if (action.Kind != ShopActionKind.Item)
                    continue;
                uint id = ++nextId;
                ids.Add(id);
                deliverables.Add(new ShopDeliverable
                {
                    Id = id,
                    ItemId = (uint)action.Param1,
                    Quantity = (uint)action.Param2,
                    Name = product.Name
                });
            }
            productDeliverables.Add(product.Id, ids.ToArray());
        }
        data.WriteInt32(enabled ? 0 : 2);
        data.WriteUInt32(enabled ? 1u : 0u);
        data.WriteUInt32((uint)products.Length);
        data.WriteUInt32((uint)deliverables.Count);
        data.WriteUInt32((uint)groups.Length);
        data.WriteUInt32((uint)products.Length);
        foreach (ShopProduct product in products)
            WriteProduct(data, product, productDeliverables[product.Id]);
        foreach (ShopDeliverable deliverable in deliverables)
            deliverable.Write(data);
        foreach (ShopGroup group in groups)
            WriteGroup(data, group);
        foreach (ShopProduct product in products)
            WriteEntry(data, product);
        return data.ToArray();
    }

    static ShopDisplay Display(ShopProduct product) => new()
    {
        FileDataId = product.Icon,
        Name1 = product.Name,
        Name3 = product.Description
    };

    static void WriteProduct(ServiceBuffer data, ShopProduct product, uint[] deliverables)
    {
        data.WriteUInt32(product.Id);
        data.WriteUInt64(Price(Math.Max(product.Price, product.NormalPrice)));
        data.WriteUInt64(Price(product.Price));
        data.WriteUInt32((uint)deliverables.Length);
        data.WriteInt32(product.Actions.Any(a => a.Kind == ShopActionKind.RealmTransfer) ? 15 : 0);
        data.WriteInt32(15);
        data.WriteUInt32(0);
        data.WriteUInt32(0);
        data.WriteInt32(0);
        data.WriteUInt64(0);
        foreach (uint deliverable in deliverables)
            data.WriteUInt32(deliverable);
        data.WriteBit(true);
        data.FlushBits();
        Display(product).Write(data);
    }

    static void WriteGroup(ServiceBuffer data, ShopGroup group)
    {
        data.WriteUInt32(group.Id);
        data.WriteInt32(group.Icon);
        data.WriteUInt8(0);
        data.WriteInt32(group.Order);
        data.WriteInt32(0);
        data.WriteUInt32(0);
        StringSize(data, group.Name, 8);
        data.WriteBits(0, 24);
        data.FlushBits();
        data.WriteSizedString(group.Name);
    }

    static void WriteEntry(ServiceBuffer data, ShopProduct product)
    {
        data.WriteUInt32(product.Id);
        data.WriteUInt32(product.GroupId);
        data.WriteUInt32(product.Id);
        data.WriteInt32(product.Order);
        data.WriteInt32(0);
        data.WriteUInt8(0);
        data.WriteBit(true);
        data.FlushBits();
        Display(product).Write(data);
    }

    public static byte[] DistributionList(IReadOnlyList<ShopDistribution> tokens, bool enabled)
    {
        if (tokens.Count > 2047)
            throw new InvalidDataException("Too many unassigned shop tokens");
        var data = new ServiceBuffer();
        data.WriteInt32(enabled ? 0 : 2);
        data.WriteBits((ulong)tokens.Count, 11);
        data.FlushBits();
        foreach (ShopDistribution token in tokens)
            token.Write(data);
        return data.ToArray();
    }

    public static ShopAssignRequest ReadAssign(ServiceBuffer data)
    {
        uint clientToken = data.ReadUInt32();
        ulong distribution = data.ReadUInt64();
        var target = ServiceBuffer.ReadGuid(data);
        uint choice = data.ReadUInt32();
        End(data);
        return new(clientToken, distribution, target.High, target.Low, choice);
    }

    public static byte[] AssignResponse(uint clientToken, ulong id, int result)
    {
        var data = new ServiceBuffer();
        data.WriteInt32(result);
        data.WriteUInt32(clientToken);
        data.WriteUInt64(id);
        return data.ToArray();
    }
}

sealed class ShopDisplay
{
    public uint? FileDataId { get; init; }
    public uint? ModelSceneId { get; init; }
    public string Name1 { get; init; } = "";
    public string Name2 { get; init; } = "";
    public string Name3 { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public string Instructions { get; init; } = "";
    public uint? Flags { get; init; }
    public uint? OverrideTextColor { get; init; }
    public uint? OverrideTexture { get; init; }
    public uint? OverrideBackground { get; init; }
    public string Disclaimer { get; init; } = "";
    public string Link { get; init; } = "";
    public ShopDisplayCard[] Cards { get; init; } = [];
    public int CardType { get; init; }
    public int BannerType { get; init; }
    public int Quantity { get; init; }

    public void Write(ServiceBuffer data)
    {
        data.WriteBit(FileDataId.HasValue);
        data.WriteBit(ModelSceneId.HasValue);
        BattlePayPackets.StringSize(data, Name1, 10);
        BattlePayPackets.StringSize(data, Name2, 10);
        BattlePayPackets.StringSize(data, Name3, 13);
        BattlePayPackets.StringSize(data, Tooltip, 13);
        BattlePayPackets.StringSize(data, Instructions, 13);
        data.WriteBit(Flags.HasValue);
        data.WriteBit(OverrideTextColor.HasValue);
        data.WriteBit(OverrideTexture.HasValue);
        data.WriteBit(OverrideBackground.HasValue);
        BattlePayPackets.StringSize(data, Disclaimer, 13);
        BattlePayPackets.StringSize(data, Link, 12);
        data.FlushBits();
        data.WriteUInt32((uint)Cards.Length);
        data.WriteInt32(CardType);
        data.WriteInt32(BannerType);
        data.WriteInt32(Quantity);
        if (FileDataId is uint file)
            data.WriteUInt32(file);
        if (ModelSceneId is uint scene)
            data.WriteUInt32(scene);
        data.WriteSizedString(Name1);
        data.WriteSizedString(Name2);
        data.WriteSizedString(Name3);
        data.WriteSizedString(Tooltip);
        data.WriteSizedString(Instructions);
        if (Flags is uint flags)
            data.WriteUInt32(flags);
        if (OverrideTextColor is uint color)
            data.WriteUInt32(color);
        if (OverrideTexture is uint texture)
            data.WriteUInt32(texture);
        if (OverrideBackground is uint background)
            data.WriteUInt32(background);
        data.WriteSizedString(Disclaimer);
        data.WriteSizedString(Link);
        foreach (ShopDisplayCard card in Cards)
        {
            BattlePayPackets.StringSize(data, card.Title, 10);
            data.FlushBits();
            data.WriteUInt32(card.CreatureDisplay);
            data.WriteUInt32(card.ModelScene);
            data.WriteUInt32(card.TransmogSet);
            data.WriteSizedString(card.Title);
        }
    }
}

readonly record struct ShopDisplayCard(string Title, uint CreatureDisplay, uint ModelScene, uint TransmogSet);

sealed class ShopDeliverable
{
    public uint Id { get; init; }
    public int Type { get; init; }
    public uint ItemId { get; init; }
    public uint Quantity { get; init; }
    public uint MountSpell { get; init; }
    public uint PetCreature { get; init; }
    public int BoostId { get; init; }
    public int Flags { get; init; }
    public uint TransmogAppearance { get; init; }
    public uint TransmogSet { get; init; }
    public uint TitleId { get; init; }
    public uint Enchantment { get; init; }
    public uint WarbandScene { get; init; }
    public string Name { get; init; } = "";
    public bool AlreadyOwns { get; init; }
    public ShopDisplay? Display { get; init; }

    public void Write(ServiceBuffer data)
    {
        data.WriteUInt32(Id);
        data.WriteInt32(Type);
        data.WriteUInt32(ItemId);
        data.WriteUInt32(Quantity);
        data.WriteUInt32(MountSpell);
        data.WriteUInt32(PetCreature);
        data.WriteInt32(BoostId);
        data.WriteInt32(Flags);
        data.WriteUInt32(TransmogAppearance);
        data.WriteUInt32(TransmogSet);
        data.WriteUInt32(TitleId);
        data.WriteUInt32(Enchantment);
        data.WriteUInt32(WarbandScene);
        BattlePayPackets.StringSize(data, Name, 8);
        data.WriteBit(AlreadyOwns);
        data.WriteBit(false);
        data.WriteBits(0, 7);
        data.WriteBit(Display != null);
        data.FlushBits();
        data.WriteSizedString(Name);
        Display?.Write(data);
    }
}

sealed record ShopDistribution(ulong Id, uint BoostType, ulong AccountGuidHigh,
    ulong AccountGuidLow, ulong TargetHigh = 0, ulong TargetLow = 0, int Status = 1)
{
    public const ulong DistributionBase = 0x100000000;

    public void Write(ServiceBuffer data)
    {
        data.WriteUInt64(DistributionBase + Id);
        data.WriteInt32(Status);
        data.WriteUInt32(800000 + BoostType);
        ServiceBuffer.WriteGuid(data, AccountGuidHigh, AccountGuidLow);
        ServiceBuffer.WriteGuid(data, TargetHigh, TargetLow);
        data.WriteUInt32(0);
        data.WriteUInt32(0);
        data.WriteUInt64(DistributionBase + Id);
        data.WriteUInt32(0);
        data.WriteBit(true);
        data.WriteBit(false);
        data.FlushBits();
        new ShopDeliverable
        {
            Id = 800000 + BoostType,
            Type = 1,
            BoostId = (int)BoostType,
            Name = "Character Boost"
        }.Write(data);
    }
}

enum ShopActionKind : byte
{
    Item = 1,
    Gold = 2,
    Spell = 3,
    Service = 4,
    Profession = 5,
    Level = 6,
    Title = 7,
    Reputation = 8,
    RealmTransfer = 9,
    BoostToken = 10
}

readonly record struct ShopAction(ShopActionKind Kind, int Param1, int Param2);
readonly record struct ShopGroup(uint Id, string Name, int Icon, int Order);
sealed record ShopProduct(uint Id, uint GroupId, string Name, string Description,
    uint Icon, uint Price, uint NormalPrice, int Order, ShopAction[] Actions);
sealed record ShopCatalog(ShopGroup[] Groups, ShopProduct[] Products);
readonly record struct ShopPurchase(ulong Id, int Status, int Result, uint ProductId,
    ulong BasePrice, ulong UserPrice, long Created, string Wallet);
readonly record struct ShopStartRequest(uint ClientToken, uint ProductId, ulong TargetHigh,
    ulong TargetLow, uint Unknown, string Unknown1, string Unknown2, string Unknown3, bool IsVas = false);
readonly record struct ShopConfirmRequest(uint ServerToken, ulong Price, bool Accepted);
readonly record struct ShopAssignRequest(uint ClientToken, ulong DistributionId,
    ulong TargetHigh, ulong TargetLow, uint Choice);
sealed record ShopPendingPurchase(ulong Id, uint ServerToken, ulong Target, ShopProduct Product,
    long Created, long ExpiresAt, bool IsVas = false);

