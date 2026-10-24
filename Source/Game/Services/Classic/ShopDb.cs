#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Framework.Constants;
using Framework.Database;
using Game.Entities;
using System.Collections.Concurrent;
using System.Globalization;
using MySqlConnector;

namespace Game.Services.Classic;

// Account points and character deliveries share one InnoDB transaction on this
// server. An interrupted purchase therefore cannot debit points without its mail.
static class ShopDb
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> SchemaGates = new();
    static readonly ConcurrentDictionary<string, bool> Schemas = new();

    internal static string Schema(string value)
    {
        if (value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new InvalidDataException("Invalid database schema name");
        return "`" + value + "`";
    }

    public static async Task Ensure(ServiceConfiguration cfg)
    {
        string key = cfg.Database.Connection(cfg.Database.Characters);
        if (Schemas.ContainsKey(key))
            return;
        SemaphoreSlim gate = SchemaGates.GetOrAdd(key, _ => new(1, 1));
        await gate.WaitAsync();
        try
        {
            if (Schemas.ContainsKey(key))
                return;
            string auth = Schema(cfg.Database.Auth);
            string world = Schema(cfg.Database.World);
            await using var connection = new MySqlConnection(key);
            await connection.OpenAsync();
            string[] statements =
            [
                $"CREATE TABLE IF NOT EXISTS {auth}.account_shop_points (AccountID INT UNSIGNED NOT NULL PRIMARY KEY, Points INT UNSIGNED NOT NULL DEFAULT 0, Premium TINYINT UNSIGNED NOT NULL DEFAULT 0, GoldMarketBanned TINYINT UNSIGNED NOT NULL DEFAULT 0, GoldMarketBanReason VARCHAR(255) NOT NULL DEFAULT '') ENGINE=InnoDB",
                $"CREATE TABLE IF NOT EXISTS {auth}.shop_purchase_log (ID BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, AccountID INT UNSIGNED NOT NULL, ProductID INT UNSIGNED NOT NULL, Price INT UNSIGNED NOT NULL, CharacterGUID BIGINT UNSIGNED NOT NULL, Time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP, KEY idx_account(AccountID)) ENGINE=InnoDB",
                $"CREATE TABLE IF NOT EXISTS {auth}.account_boost_token (ID BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, AccountID INT UNSIGNED NOT NULL, BoostType INT UNSIGNED NOT NULL DEFAULT 6, Source VARCHAR(64) NOT NULL DEFAULT '', UsedRealm INT UNSIGNED NOT NULL DEFAULT 0, UsedGuid BIGINT UNSIGNED NOT NULL DEFAULT 0, UsedTime TIMESTAMP NULL DEFAULT NULL, Created TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP, KEY idx_account(AccountID)) ENGINE=InnoDB",
                $"CREATE TABLE IF NOT EXISTS {auth}.shop_config (Name VARCHAR(64) NOT NULL PRIMARY KEY, Value VARCHAR(255) NOT NULL DEFAULT '') ENGINE=InnoDB",
                $"CREATE TABLE IF NOT EXISTS {world}.shop_group (ID INT UNSIGNED NOT NULL PRIMARY KEY, Name VARCHAR(255) NOT NULL DEFAULT '', IconFileDataID INT NOT NULL DEFAULT 0, Ordering INT NOT NULL DEFAULT 0) ENGINE=InnoDB",
                $"CREATE TABLE IF NOT EXISTS {world}.shop_product (ID INT UNSIGNED NOT NULL PRIMARY KEY, GroupID INT UNSIGNED NOT NULL, Name VARCHAR(255) NOT NULL DEFAULT '', Description TEXT NOT NULL, IconFileDataID INT NOT NULL DEFAULT 0, Price INT UNSIGNED NOT NULL DEFAULT 0, NormalPrice INT UNSIGNED NOT NULL DEFAULT 0, Ordering INT NOT NULL DEFAULT 0, Enabled TINYINT UNSIGNED NOT NULL DEFAULT 1) ENGINE=InnoDB",
                $"CREATE TABLE IF NOT EXISTS {world}.shop_product_action (ProductID INT UNSIGNED NOT NULL, `Index` TINYINT UNSIGNED NOT NULL DEFAULT 0, Action TINYINT UNSIGNED NOT NULL, Param1 INT NOT NULL DEFAULT 0, Param2 INT NOT NULL DEFAULT 0, PRIMARY KEY(ProductID,`Index`)) ENGINE=InnoDB",
                $"CREATE TABLE IF NOT EXISTS {world}.shop_boost_item (BoostType INT UNSIGNED NOT NULL DEFAULT 6, ClassID TINYINT UNSIGNED NOT NULL DEFAULT 0, ItemID INT UNSIGNED NOT NULL, Count INT UNSIGNED NOT NULL DEFAULT 1, PRIMARY KEY(BoostType,ClassID,ItemID)) ENGINE=InnoDB",
                "CREATE TABLE IF NOT EXISTS shop_purchase_receipt (PurchaseID BIGINT UNSIGNED NOT NULL PRIMARY KEY, AccountID INT UNSIGNED NOT NULL, ProductID INT UNSIGNED NOT NULL, CharacterGUID BIGINT UNSIGNED NOT NULL, Price INT UNSIGNED NOT NULL, Created BIGINT NOT NULL, KEY idx_account(AccountID)) ENGINE=InnoDB",
                "CREATE TABLE IF NOT EXISTS shop_gold_listing (ID INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, SellerAccount INT UNSIGNED NOT NULL, SellerGuid BIGINT UNSIGNED NOT NULL, Gold INT UNSIGNED NOT NULL, Price INT UNSIGNED NOT NULL, Time TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP) ENGINE=InnoDB"
            ];
            foreach (string statement in statements)
            {
                await using var command = new MySqlCommand(statement, connection);
                await command.ExecuteNonQueryAsync();
            }
            // Seed only an empty catalog. Existing administrator products are preserved.
            await using (var count = new MySqlCommand($"SELECT COUNT(*) FROM {world}.shop_product", connection))
            {
                if (Convert.ToInt64(await count.ExecuteScalarAsync()) == 0)
                {
                    await using var transaction = await connection.BeginTransactionAsync();
                    await Execute(connection, transaction,
                        $"INSERT IGNORE INTO {world}.shop_group VALUES (1,'General',134414,1),(2,'Services',136243,2)");
                    await Execute(connection, transaction,
                        $"INSERT IGNORE INTO {world}.shop_product VALUES (1,1,'Hearthstone','A spare Hearthstone, sent by mail.',134414,1,0,1,1),(2,1,'100 Gold','100 gold, sent by mail.',133784,10,0,2,1),(10,2,'Name Change','Change this character name at character selection.',136243,10,0,1,1),(11,2,'Appearance Change','Change this character appearance at character selection.',136243,10,0,2,1),(12,2,'Race Change','Change this character race at character selection.',136243,20,0,3,1),(13,2,'Faction Change','Change this character faction at character selection.',136243,30,0,4,1)");
                    await Execute(connection, transaction,
                        $"INSERT IGNORE INTO {world}.shop_product_action VALUES (1,0,1,6948,1),(2,0,2,1000000,0),(10,0,4,1,0),(11,0,4,2,0),(12,0,4,3,0),(13,0,4,4,0)");
                    await transaction.CommitAsync();
                }
            }
            Schemas[key] = true;
            Log.outInfo(LogFilter.Server, "World shop database tables ready.");
        }
        finally
        {
            gate.Release();
        }
    }

    public static async Task<ShopCatalog> Catalog(ServiceConfiguration cfg)
    {
        await Ensure(cfg);
        string world = Schema(cfg.Database.World);
        await using var connection = new MySqlConnection(cfg.Database.Connection(cfg.Database.Characters));
        await connection.OpenAsync();
        var groups = new List<ShopGroup>();
        var products = new List<ShopProduct>();
        var actions = new Dictionary<uint, List<ShopAction>>();
        await using (var command = new MySqlCommand($"SELECT ProductID,Action,Param1,Param2 FROM {world}.shop_product_action ORDER BY ProductID,`Index`", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                uint id = reader.GetUInt32(0);
                if (!actions.TryGetValue(id, out List<ShopAction>? list))
                    actions[id] = list = [];
                list.Add(new((ShopActionKind)reader.GetByte(1), reader.GetInt32(2), reader.GetInt32(3)));
            }
        }
        await using (var command = new MySqlCommand($"SELECT ID,Name,IconFileDataID,Ordering FROM {world}.shop_group ORDER BY Ordering,ID", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                groups.Add(new(reader.GetUInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        }
        await using (var command = new MySqlCommand($"SELECT ID,GroupID,Name,Description,IconFileDataID,Price,NormalPrice,Ordering FROM {world}.shop_product WHERE Enabled<>0 ORDER BY Ordering,ID", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                uint id = reader.GetUInt32(0);
                ShopAction[] productActions = actions.GetValueOrDefault(id)?.ToArray() ?? [];
                if (productActions.Length == 0 || productActions.Any(a => !ValidAction(a)))
                {
                    Log.outWarn(LogFilter.SessionRpc, $"Shop product {id} has invalid or unsupported actions; omitted from catalog.");
                    continue;
                }
                products.Add(new(id, reader.GetUInt32(1), reader.GetString(2), reader.GetString(3),
                    checked((uint)Math.Max(0, reader.GetInt32(4))), reader.GetUInt32(5),
                    reader.GetUInt32(6), reader.GetInt32(7), productActions));
            }
        }
        await using (var command = new MySqlCommand("SELECT ID,Gold,Price FROM shop_gold_listing ORDER BY Gold DESC,Price,ID LIMIT 100", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                uint id = reader.GetUInt32(0);
                uint gold = reader.GetUInt32(1);
                if (id >= 100000000 || gold is 0 or > 200000)
                    continue;
                products.Add(new(100000000 + id, 1000, $"{gold:N0} Gold", "Gold Market purchase, delivered by mail.",
                    133784, reader.GetUInt32(2), 0, products.Count,
                    [new(ShopActionKind.Gold, checked((int)gold * 10000), 0)]));
            }
        }
        if (products.Any(p => p.GroupId == 1000))
            groups.Add(new(1000, "Gold Market", 133784, 1000));
        return new(groups.ToArray(), products.Where(p => groups.Any(g => g.Id == p.GroupId)).ToArray());
    }

    public static bool ValidAction(ShopAction action) => action.Kind switch
    {
        ShopActionKind.Item => action.Param1 > 0 && action.Param2 is > 0 and <= 10000,
        ShopActionKind.Gold => action.Param1 > 0,
        ShopActionKind.Spell => action.Param1 > 0,
        ShopActionKind.Service => action.Param1 is >= 1 and <= 4,
        ShopActionKind.Profession => action.Param1 is > 0 and <= ushort.MaxValue && action.Param2 is >= 0 and <= 300,
        ShopActionKind.Level => action.Param1 is > 0 and <= 60,
        ShopActionKind.Title => action.Param1 > 0,
        ShopActionKind.Reputation => action.Param1 > 0 && action.Param2 is >= -42000 and <= 42000,
        ShopActionKind.BoostToken => action.Param1 == 6,
        _ => false
    };

    public static async Task<uint> Points(ServiceConfiguration cfg, uint account)
    {
        await Ensure(cfg);
        await using var connection = new MySqlConnection(cfg.Database.Connection(cfg.Database.Auth));
        await connection.OpenAsync();
        await using var command = new MySqlCommand("SELECT Points FROM account_shop_points WHERE AccountID=@account", connection);
        command.Parameters.AddWithValue("@account", account);
        object? value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? 0 : Convert.ToUInt32(value, CultureInfo.InvariantCulture);
    }

    public static async Task<bool> Owns(ServiceConfiguration cfg, uint account, ulong character)
    {
        if (character == 0)
            return false;
        await using var connection = new MySqlConnection(cfg.Database.Connection(cfg.Database.Characters));
        await connection.OpenAsync();
        await using var command = new MySqlCommand("SELECT 1 FROM characters WHERE guid=@guid AND account=@account AND deleteDate IS NULL", connection);
        command.Parameters.AddWithValue("@guid", character);
        command.Parameters.AddWithValue("@account", account);
        return await command.ExecuteScalarAsync() != null;
    }

    public static async Task<ShopPurchase[]> Purchases(ServiceConfiguration cfg, uint account)
    {
        await Ensure(cfg);
        await using var connection = new MySqlConnection(cfg.Database.Connection(cfg.Database.Characters));
        await connection.OpenAsync();
        var result = new List<ShopPurchase>();
        await using var command = new MySqlCommand("SELECT PurchaseID,ProductID,Price,Created FROM shop_purchase_receipt WHERE AccountID=@account ORDER BY Created DESC LIMIT 100", connection);
        command.Parameters.AddWithValue("@account", account);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ulong price = BattlePayPackets.Price(reader.GetUInt32(2));
            result.Add(new(reader.GetUInt64(0), 6, 0, reader.GetUInt32(1), price, price,
                reader.GetInt64(3), "Shop points"));
        }
        return result.ToArray();
    }

    public static async Task<ShopDistribution[]> Distributions(ServiceConfiguration cfg, uint account)
    {
        await Ensure(cfg);
        await using var connection = new MySqlConnection(cfg.Database.Connection(cfg.Database.Auth));
        await connection.OpenAsync();
        var tokens = new List<ShopDistribution>();
        await using var command = new MySqlCommand("SELECT ID,BoostType FROM account_boost_token WHERE AccountID=@account AND UsedTime IS NULL ORDER BY ID LIMIT 2047", connection);
        command.Parameters.AddWithValue("@account", account);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tokens.Add(new(reader.GetUInt64(0), reader.GetUInt32(1), 29UL << 58, account));
        return tokens.ToArray();
    }

    public static async Task<bool> Commit(ServiceConfiguration cfg, uint account, ShopPendingPurchase pending)
    {
        await Ensure(cfg);
        string auth = Schema(cfg.Database.Auth);
        await using var connection = new MySqlConnection(cfg.Database.Connection(cfg.Database.Characters));
        await connection.OpenAsync();
        await using var idLock = await ShopIdLock.Acquire(connection);
        await using var transaction = await connection.BeginTransactionAsync();
        // Lock the owning character before touching points or generating delivery IDs.
        await using (var character = Command(connection, transaction,
            "SELECT account FROM characters WHERE guid=@target AND deleteDate IS NULL FOR UPDATE", ("@target", pending.Target)))
        {
            object? owner = await character.ExecuteScalarAsync();
            if (owner is null or DBNull || Convert.ToUInt32(owner) != account)
                return false;
        }
        await using (var duplicate = Command(connection, transaction,
            "SELECT AccountID FROM shop_purchase_receipt WHERE PurchaseID=@id", ("@id", pending.Id)))
        {
            object? previous = await duplicate.ExecuteScalarAsync();
            if (previous != null)
                return Convert.ToUInt32(previous) == account;
        }
        await Execute(connection, transaction,
            $"INSERT IGNORE INTO {auth}.account_shop_points(AccountID,Points) VALUES(@account,0)", ("@account", account));
        int debited = await Execute(connection, transaction,
            $"UPDATE {auth}.account_shop_points SET Points=Points-@price WHERE AccountID=@account AND Points>=@price",
            ("@price", pending.Product.Price), ("@account", account));
        if (debited != 1)
            return false;
        if (pending.Product.Id >= 100000000)
        {
            if (!await BuyGoldListing(connection, transaction, auth, account, pending))
                return false;
        }
        await Deliver(connection, transaction, cfg, account, pending.Target, pending.Product);
        await Execute(connection, transaction,
            "INSERT INTO shop_purchase_receipt VALUES(@id,@account,@product,@target,@price,@created)",
            ("@id", pending.Id), ("@account", account), ("@product", pending.Product.Id),
            ("@target", pending.Target), ("@price", pending.Product.Price), ("@created", pending.Created));
        await Execute(connection, transaction,
            $"INSERT INTO {auth}.shop_purchase_log(AccountID,ProductID,Price,CharacterGUID) VALUES(@account,@product,@price,@target)",
            ("@account", account), ("@product", pending.Product.Id), ("@price", pending.Product.Price), ("@target", pending.Target));
        await transaction.CommitAsync();
        return true;
    }

    static async Task<bool> BuyGoldListing(MySqlConnection connection, MySqlTransaction transaction,
        string auth, uint buyer, ShopPendingPurchase pending)
    {
        uint seller;
        uint gold;
        uint price;
        await using (var command = Command(connection, transaction,
            "SELECT SellerAccount,Gold,Price FROM shop_gold_listing WHERE ID=@id FOR UPDATE",
            ("@id", pending.Product.Id - 100000000)))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
                return false;
            seller = reader.GetUInt32(0);
            gold = reader.GetUInt32(1);
            price = reader.GetUInt32(2);
        }
        if (seller == buyer || price != pending.Product.Price ||
            pending.Product.Actions[0].Param1 != (long)gold * 10000)
            return false;
        int feePercent = 10;
        await using (var fee = new MySqlCommand($"SELECT Value FROM {auth}.shop_config WHERE Name='GoldMarketFee'", connection, transaction))
        {
            object? value = await fee.ExecuteScalarAsync();
            if (value != null && int.TryParse(value.ToString(), out int configured))
                feePercent = Math.Clamp(configured, 0, 100);
        }
        uint feeAmount = feePercent == 0 ? 0 : Math.Max(1u, (uint)((ulong)price * (uint)feePercent / 100));
        uint proceeds = price - Math.Min(price, feeAmount);
        await Execute(connection, transaction,
            $"INSERT INTO {auth}.account_shop_points(AccountID,Points) VALUES(@seller,@points) ON DUPLICATE KEY UPDATE Points=Points+@points",
            ("@seller", seller), ("@points", proceeds));
        await Execute(connection, transaction, "DELETE FROM shop_gold_listing WHERE ID=@id",
            ("@id", pending.Product.Id - 100000000));
        return true;
    }

    static async Task Deliver(MySqlConnection connection, MySqlTransaction transaction,
        ServiceConfiguration cfg, uint account, ulong target, ShopProduct product)
    {
        ulong copper = 0;
        var attachments = new List<(uint Entry, uint Count)>();
        foreach (ShopAction action in product.Actions)
        {
            switch (action.Kind)
            {
                case ShopActionKind.Item:
                    uint entry = checked((uint)action.Param1);
                    if (!ItemStatCatalog.HasTemplate(entry))
                        throw new InvalidDataException($"Shop item {entry} is absent");
                    uint count = checked((uint)action.Param2);
                    uint stack = (uint)Math.Max(1, ItemSpells.MaximumStack(entry));
                    while (count != 0)
                    {
                        uint quantity = Math.Min(stack, count);
                        attachments.Add((entry, quantity));
                        count -= quantity;
                    }
                    break;
                case ShopActionKind.Gold:
                    copper = checked(copper + (uint)action.Param1);
                    break;
                case ShopActionKind.Service:
                    uint flag = action.Param1 switch { 1 => 1u, 2 => 8u, 3 => 128u, 4 => 64u, _ => 0u };
                    await Execute(connection, transaction,
                        "UPDATE characters SET at_login=at_login|@flag WHERE guid=@target",
                        ("@flag", flag), ("@target", target));
                    break;
                case ShopActionKind.BoostToken:
                    await Execute(connection, transaction,
                        $"INSERT INTO {Schema(cfg.Database.Auth)}.account_boost_token(AccountID,BoostType,Source) VALUES(@account,@type,'shop')",
                        ("@account", account), ("@type", action.Param1));
                    break;
                default:
                    // Live character changes are persisted in the same transaction;
                    // the session refresh then publishes their projections to clients.
                    await PersistCharacterAction(connection, transaction, target, action);
                    break;
            }
        }
        if (attachments.Count > 120)
            throw new InvalidDataException("Shop product exceeds ten mails of attachments");
        if (attachments.Count == 0 && copper == 0)
            return;
        int offset = 0;
        do
        {
            var batch = attachments.Skip(offset).Take(12).ToArray();
            await SendMail(connection, transaction, target, product.Name, copper, batch);
            copper = 0;
            offset += batch.Length;
        } while (offset < attachments.Count);
    }

    static async Task PersistCharacterAction(MySqlConnection connection, MySqlTransaction transaction,
        ulong target, ShopAction action)
    {
        switch (action.Kind)
        {
            case ShopActionKind.Spell:
                await Execute(connection, transaction,
                    "INSERT INTO character_spell(guid,spell,active,disabled) VALUES(@target,@spell,1,0) ON DUPLICATE KEY UPDATE active=1,disabled=0",
                    ("@target", target), ("@spell", action.Param1));
                break;
            case ShopActionKind.Level:
                await Execute(connection, transaction,
                    "UPDATE characters SET level=GREATEST(level,@level),xp=0 WHERE guid=@target",
                    ("@target", target), ("@level", action.Param1));
                break;
            case ShopActionKind.Profession:
                int value = action.Param2 == 0 ? 300 : action.Param2;
                await Execute(connection, transaction,
                    "INSERT INTO character_skills(guid,skill,value,max) VALUES(@target,@skill,@value,@value) ON DUPLICATE KEY UPDATE value=GREATEST(value,@value),max=GREATEST(max,@value)",
                    ("@target", target), ("@skill", action.Param1), ("@value", value));
                break;
            case ShopActionKind.Reputation:
                await Execute(connection, transaction,
                    "INSERT INTO character_reputation(guid,faction,standing,flags) VALUES(@target,@faction,@standing,1) ON DUPLICATE KEY UPDATE standing=@standing",
                    ("@target", target), ("@faction", action.Param1), ("@standing", action.Param2));
                break;
            case ShopActionKind.Title:
                if (!AchievementCatalog.TryGetTitleMask((uint)action.Param1, out int mask) || mask < 0 || mask > 4095)
                    throw new InvalidDataException("Unknown shop title");
                ulong[] titles;
                await using (var read = Command(connection, transaction,
                    "SELECT knownTitles FROM characters WHERE guid=@target", ("@target", target)))
                    titles = GameDb.ParseCharacterBitVector(Convert.ToString(await read.ExecuteScalarAsync()));
                int word = mask / 64;
                if (titles.Length <= word)
                    Array.Resize(ref titles, word + 1);
                titles[word] |= 1UL << (mask & 63);
                await Execute(connection, transaction,
                    "UPDATE characters SET knownTitles=@titles WHERE guid=@target",
                    ("@titles", GameDb.EncodeCharacterBitVector(titles)), ("@target", target));
                break;
            default:
                throw new InvalidDataException("Unrecognized shop action");
        }
    }

    internal static async Task SendMail(MySqlConnection connection, MySqlTransaction transaction,
        ulong target, string name, ulong copper, (uint Entry, uint Count)[] attachments)
    {
        ulong mail = await NextId(connection, transaction, "mail", "id");
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await Execute(connection, transaction,
            "INSERT INTO mail(id,messageType,stationery,mailTemplateId,sender,receiver,subject,body,has_items,expire_time,deliver_time,money,cod,checked) VALUES(@id,0,61,0,0,@target,'Shop purchase',@body,@items,@expires,@now,@money,0,0)",
            ("@id", mail), ("@target", target), ("@body", "Thank you for buying " + name + "."),
            ("@items", attachments.Length != 0), ("@expires", now + 30 * 86400), ("@now", now), ("@money", copper));
        foreach (var attachment in attachments)
        {
            Item item = Item.CreateItem((int)attachment.Entry, (int)attachment.Count, ItemContext.None);
            if (item == null) throw new InvalidDataException($"Cannot create shop item {attachment.Entry}");
            ulong itemGuid = (ulong)item.GetGUID().GetCounter();
            try
            {
                item.SetOwnerGUID(ObjectGuid.Create(HighGuid.Player, (long)target));
                var save = new SQLTransaction();
                item.SaveToDB(save);
                foreach (var statement in save.commands)
                {
                    statement.Connection = connection;
                    statement.Transaction = transaction;
                    await statement.ExecuteNonQueryAsync();
                    statement.Dispose();
                }
            }
            finally { item.Dispose(); }
            await Execute(connection, transaction,
                "INSERT INTO mail_items(mail_id,item_guid,receiver) VALUES(@mail,@item,@target)",
                ("@mail", mail), ("@item", itemGuid), ("@target", target));
        }
    }

    internal static Task<ulong> NextId(MySqlConnection connection, MySqlTransaction transaction,
        string table, string column)
    {
        if (table != "mail" || column != "id") throw new InvalidDataException("Unsupported delivery ID allocation");
        return Task.FromResult((ulong)Global.ObjectMgr.GenerateMailID());
    }

    internal static MySqlCommand Command(MySqlConnection connection, MySqlTransaction transaction,
        string sql, params (string Name, object Value)[] parameters)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return command;
    }

    internal static async Task<int> Execute(MySqlConnection connection, MySqlTransaction transaction,
        string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }
}


