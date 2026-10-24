#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Framework.Constants;
using Framework.Database;
using Game.DataStorage;
using Game.Entities;
using System.Threading.Tasks;
using MySqlConnector;

namespace Game.Services.Classic;

sealed class ServiceConfiguration
{
    public static ServiceConfiguration Current { get; } = new();
    public ServiceDatabases Database { get; } = new();
    public uint RealmID => (uint)Global.WorldMgr.GetRealmId().Index;
    public uint RealmAddress => Global.WorldMgr.GetRealmId().GetAddress();
    public string RealmName => Global.WorldMgr.GetRealm().Name;
    public bool ShopEnabled => WorldConfig.Values[WorldCfg.FeatureSystemBpayStoreEnabled].Bool;
    public int ShopBoostGold => 100;
}

sealed class ServiceDatabases
{
    public string Auth => DB.Login.GetDatabaseName();
    public string Characters => DB.Characters.GetDatabaseName();
    public string World => DB.World.GetDatabaseName();
    public string Connection(string schema)
    {
        using var connection = schema == Auth ? DB.Login.CreateConnection()
            : schema == World ? DB.World.CreateConnection() : DB.Characters.CreateConnection();
        return connection.ConnectionString;
    }
}

static class ItemStatCatalog
{
    public static bool HasTemplate(uint entry) => Global.ObjectMgr.GetItemTemplate((int)entry) != null;
    public static int MaxDurability(uint entry) => Global.ObjectMgr.GetItemTemplate((int)entry)?.MaxDurability ?? 0;
}

static class ItemSpells
{
    public static int MaximumStack(uint entry) => Global.ObjectMgr.GetItemTemplate((int)entry)?.GetMaxStackSize() ?? 1;
    public static int[] InitialCharges(uint entry)
    {
        int[] charges = new int[5];
        var proto = Global.ObjectMgr.GetItemTemplate((int)entry);
        if (proto != null)
            foreach (var effect in proto.Effects)
                if (effect.LegacySlotIndex < charges.Length) charges[effect.LegacySlotIndex] = effect.Charges;
        return charges;
    }
}

static class AchievementCatalog
{
    public static bool TryGetTitleMask(uint id, out int mask)
    {
        var title = CliDB.CharTitlesStorage.LookupByKey((int)id);
        mask = title == null ? -1 : title.MaskID;
        return title != null;
    }
}

static class GameDb
{
    internal sealed record ShopCharacter(ulong Guid, string Name, byte Race, byte Class, byte Gender, byte Level);
    public static async Task<List<ShopCharacter>> LoadCharacters(ServiceConfiguration cfg, uint account)
    {
        var result = new List<ShopCharacter>();
        await using var connection = DB.Characters.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand("SELECT guid,name,race,class,gender,level FROM characters WHERE account=@account AND deleteDate IS NULL ORDER BY guid", connection);
        command.Parameters.AddWithValue("@account", account);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) result.Add(new(reader.GetUInt64(0), reader.GetString(1), reader.GetByte(2), reader.GetByte(3), reader.GetByte(4), reader.GetByte(5)));
        return result;
    }
    public static ulong[] ParseCharacterBitVector(string? encoded) => (encoded ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => ulong.Parse(value, CultureInfo.InvariantCulture)).ToArray();
    public static string EncodeCharacterBitVector(IReadOnlyList<ulong> values) => string.Join(' ', values);
}
