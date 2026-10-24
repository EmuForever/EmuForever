#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Framework.Constants;
using Game.DataStorage;
using MySqlConnector;

namespace Game.Services.Classic;

sealed class ShopIdLock : IAsyncDisposable
{
    readonly MySqlConnection _connection;

    ShopIdLock(MySqlConnection connection) => _connection = connection;

    public static async Task<ShopIdLock> Acquire(MySqlConnection connection)
    {
        await using var command = new MySqlCommand("SELECT GET_LOCK('ForeverWorld.achievement-mail-id',5)", connection);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) != 1)
            throw new InvalidDataException("Could not acquire character delivery ID lock");
        return new(connection);
    }

    public async ValueTask DisposeAsync()
    {
        await using var command = new MySqlCommand("SELECT RELEASE_LOCK('ForeverWorld.achievement-mail-id')", _connection);
        await command.ExecuteScalarAsync();
    }
}

static class ShopBoostService
{
    public static async Task<bool> Assign(ServiceConfiguration cfg, uint account, ShopAssignRequest request)
    {
        if (request.DistributionId <= ShopDistribution.DistributionBase ||
            request.DistributionId - ShopDistribution.DistributionBase > uint.MaxValue)
            return false;
        ulong tokenId = request.DistributionId - ShopDistribution.DistributionBase;
        await ShopDb.Ensure(cfg);
        string auth = ShopDb.Schema(cfg.Database.Auth);
        string world = ShopDb.Schema(cfg.Database.World);
        await using var connection = new MySqlConnection(cfg.Database.Connection(cfg.Database.Characters));
        await connection.OpenAsync();
        await using var idLock = await ShopIdLock.Acquire(connection);
        await using var transaction = await connection.BeginTransactionAsync();
        uint boost;
        await using (var command = ShopDb.Command(connection, transaction,
            $"SELECT BoostType FROM {auth}.account_boost_token WHERE ID=@id AND AccountID=@account AND UsedTime IS NULL FOR UPDATE",
            ("@id", tokenId), ("@account", account)))
        {
            object? value = await command.ExecuteScalarAsync();
            if (value == null)
                return false;
            boost = Convert.ToUInt32(value);
        }
        if (boost != 6)
            return false;
        byte race;
        byte playerClass;
        int level;
        await using (var command = ShopDb.Command(connection, transaction,
            "SELECT race,class,level FROM characters WHERE guid=@guid AND account=@account AND online=0 AND deleteDate IS NULL FOR UPDATE",
            ("@guid", request.TargetLow), ("@account", account)))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
                return false;
            race = reader.GetByte(0);
            playerClass = reader.GetByte(1);
            level = reader.GetByte(2);
        }
        if (level >= 60)
            return false;
        if (request.Choice != 0 && CliDB.ChrSpecializationStorage.LookupByKey((int)request.Choice)?.ClassID != (Class)playerClass)
            return false;
        var stats = Global.ObjectMgr.GetPlayerLevelInfo((Race)race, (Class)playerClass, 60);
        long health = stats == null ? 0 : stats.baseHealth + Math.Min(20, stats.stats[(int)Stats.Stamina]) + Math.Max(0, stats.stats[(int)Stats.Stamina] - 20) * 10;
        if (health <= 0)
            throw new InvalidDataException("Missing boost health data");
        int mana = stats.baseMana;
        await ShopDb.Execute(connection, transaction,
            "UPDATE characters SET level=60,xp=0,health=@health,power1=@mana,playerFlags=playerFlags&~16,at_login=at_login|2 WHERE guid=@guid",
            ("@health", health), ("@mana", mana), ("@guid", request.TargetLow));
        await ShopDb.Execute(connection, transaction,
            $"UPDATE {auth}.account_boost_token SET UsedRealm=@realm,UsedGuid=@guid,UsedTime=CURRENT_TIMESTAMP WHERE ID=@id AND AccountID=@account AND UsedTime IS NULL",
            ("@realm", cfg.RealmID), ("@guid", request.TargetLow), ("@id", tokenId), ("@account", account));
        var items = new List<(uint Entry, uint Count)>();
        await using (var command = ShopDb.Command(connection, transaction,
            $"SELECT ItemID,Count FROM {world}.shop_boost_item WHERE BoostType=@type AND ClassID IN(0,@class) ORDER BY ClassID,ItemID",
            ("@type", boost), ("@class", playerClass)))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                uint entry = reader.GetUInt32(0);
                uint count = reader.GetUInt32(1);
                if (!ItemStatCatalog.HasTemplate(entry) || count == 0)
                    throw new InvalidDataException($"Invalid boost item {entry}");
                uint stack = (uint)Math.Max(1, ItemSpells.MaximumStack(entry));
                while (count > 0)
                {
                    uint amount = Math.Min(count, stack);
                    items.Add((entry, amount));
                    count -= amount;
                    if (items.Count > 120)
                        throw new InvalidDataException("Too many boost attachments");
                }
            }
        }
        ulong gold = (ulong)cfg.ShopBoostGold * 10000;
        int offset = 0;
        do
        {
            var batch = items.Skip(offset).Take(12).ToArray();
            await ShopDb.SendMail(connection, transaction, request.TargetLow, "Level 60 Character Boost", gold, batch);
            offset += batch.Length;
            gold = 0;
        } while (offset < items.Count);
        await transaction.CommitAsync();
        Log.outInfo(LogFilter.Server, $"Shop boost token {request.DistributionId} assigned to character {request.TargetLow}.");
        return true;
    }
}

