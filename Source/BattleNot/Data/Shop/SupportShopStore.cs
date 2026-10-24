using System.Collections.Concurrent;
using System.Text;
using MySqlConnector;

namespace BattleNot;

readonly record struct SupportShopAction(byte Kind, int Param1, int Param2);
sealed record SupportShopProduct(uint Id, uint Group, string Name, string Description, int Icon,
    uint Price, uint NormalPrice, int Order, bool Enabled, SupportShopAction[] Actions);
readonly record struct SupportShopGroup(uint Id, string Name, int Icon, int Order);

static class SupportShopStore
{
    static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();
    static readonly ConcurrentDictionary<string, bool> Ready = new();

    public static async Task Ensure(MySqlConnection connection)
    {
        string key = connection.ConnectionString;
        if (Ready.ContainsKey(key)) return;
        SemaphoreSlim gate = Gates.GetOrAdd(key, _ => new(1, 1));
        await gate.WaitAsync();
        try
        {
            if (Ready.ContainsKey(key)) return;
            foreach (string sql in new[]
            {
                "CREATE TABLE IF NOT EXISTS shop_group (ID INT UNSIGNED NOT NULL PRIMARY KEY,Name VARCHAR(255) NOT NULL DEFAULT '',IconFileDataID INT NOT NULL DEFAULT 0,Ordering INT NOT NULL DEFAULT 0) ENGINE=InnoDB",
                "CREATE TABLE IF NOT EXISTS shop_product (ID INT UNSIGNED NOT NULL PRIMARY KEY,GroupID INT UNSIGNED NOT NULL,Name VARCHAR(255) NOT NULL DEFAULT '',Description TEXT NOT NULL,IconFileDataID INT NOT NULL DEFAULT 0,Price INT UNSIGNED NOT NULL DEFAULT 0,NormalPrice INT UNSIGNED NOT NULL DEFAULT 0,Ordering INT NOT NULL DEFAULT 0,Enabled TINYINT UNSIGNED NOT NULL DEFAULT 1) ENGINE=InnoDB",
                "CREATE TABLE IF NOT EXISTS shop_product_action (ProductID INT UNSIGNED NOT NULL,`Index` TINYINT UNSIGNED NOT NULL DEFAULT 0,Action TINYINT UNSIGNED NOT NULL,Param1 INT NOT NULL DEFAULT 0,Param2 INT NOT NULL DEFAULT 0,PRIMARY KEY(ProductID,`Index`)) ENGINE=InnoDB"
            })
            {
                await using var command = new MySqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync();
            }
            Ready[key] = true;
        }
        finally { gate.Release(); }
    }

    public static async Task<(SupportShopGroup[] Groups, SupportShopProduct[] Products)> Read(ShopDb db)
    {
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await Ensure(connection);
        var groups = new List<SupportShopGroup>();
        var products = new List<SupportShopProduct>();
        var actions = new Dictionary<uint, List<SupportShopAction>>();
        await using (var command = new MySqlCommand("SELECT ProductID,Action,Param1,Param2 FROM shop_product_action ORDER BY ProductID,`Index`", connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                uint id = reader.GetUInt32(0);
                if (!actions.TryGetValue(id, out var values)) actions[id] = values = [];
                values.Add(new(reader.GetByte(1), reader.GetInt32(2), reader.GetInt32(3)));
            }
        await using (var command = new MySqlCommand("SELECT ID,Name,IconFileDataID,Ordering FROM shop_group ORDER BY Ordering,ID", connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                groups.Add(new(reader.GetUInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        await using (var command = new MySqlCommand("SELECT ID,GroupID,Name,Description,IconFileDataID,Price,NormalPrice,Ordering,Enabled FROM shop_product ORDER BY GroupID,Ordering,ID", connection))
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                uint id = reader.GetUInt32(0);
                products.Add(new(id, reader.GetUInt32(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4),
                    reader.GetUInt32(5), reader.GetUInt32(6), reader.GetInt32(7), reader.GetByte(8) != 0,
                    actions.GetValueOrDefault(id)?.ToArray() ?? []));
            }
        return (groups.ToArray(), products.ToArray());
    }

    public static SupportShopAction[] ParseActions(string input)
    {
        var actions = new List<SupportShopAction>();
        foreach (string line in input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] fields = line.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length != 3 || !byte.TryParse(fields[0], out byte kind) ||
                !int.TryParse(fields[1], out int first) || !int.TryParse(fields[2], out int second))
                throw new InvalidDataException("Each delivery line must contain: action, parameter 1, parameter 2.");
            var action = new SupportShopAction(kind, first, second);
            bool valid = kind switch
            {
                1 => first > 0 && second is > 0 and <= 10000,
                2 => first > 0,
                3 => first > 0,
                4 => first is >= 1 and <= 4,
                5 => first is > 0 and <= ushort.MaxValue && second is >= 0 and <= 300,
                6 => first is > 0 and <= 60,
                7 => first > 0,
                8 => first > 0 && second is >= -42000 and <= 42000,
                10 => first == 6,
                _ => false
            };
            if (!valid) throw new InvalidDataException($"Invalid delivery parameters for action {kind}.");
            actions.Add(action);
            if (actions.Count > 20) throw new InvalidDataException("A product may have at most 20 delivery actions.");
        }
        if (actions.Count == 0) throw new InvalidDataException("A product needs at least one delivery action.");
        return actions.ToArray();
    }

    public static async Task<uint> SaveProduct(ShopDb db, SupportShopProduct product, uint actor)
    {
        ValidateText(product.Name, 127, "Product name");
        if (Encoding.UTF8.GetByteCount(product.Description) > 2047) throw new InvalidDataException("Description is too long.");
        if (product.Icon < 0 || product.Id >= 100000000 || product.NormalPrice != 0 && product.NormalPrice < product.Price)
            throw new InvalidDataException("Invalid product ID, icon, or normal price.");
        ParseActions(string.Join('\n', product.Actions.Select(a => $"{a.Kind},{a.Param1},{a.Param2}")));
        uint id = await Mutate(db, async (connection, transaction) =>
        {
            if (await Scalar(connection, transaction, "SELECT ID FROM shop_group WHERE ID=@id FOR UPDATE", ("@id", product.Group)) == null)
                throw new InvalidDataException("Choose an existing shop group.");
            uint id = product.Id;
            if (id == 0)
            {
                id = Convert.ToUInt32(await Scalar(connection, transaction, "SELECT COALESCE(MAX(ID),0)+1 FROM shop_product WHERE ID<100000000"));
                if (id >= 100000000) throw new InvalidDataException("Product ID range exhausted.");
                await Execute(connection, transaction, "INSERT INTO shop_product(ID,GroupID,Name,Description) VALUES(@id,@group,'','')", ("@id", id), ("@group", product.Group));
            }
            else if (await Scalar(connection, transaction, "SELECT ID FROM shop_product WHERE ID=@id FOR UPDATE", ("@id", id)) == null)
                throw new InvalidDataException("This product no longer exists. Reload the catalog.");
            await Execute(connection, transaction,
                "UPDATE shop_product SET GroupID=@group,Name=@name,Description=@description,IconFileDataID=@icon,Price=@price,NormalPrice=@normal,Ordering=@order,Enabled=@enabled WHERE ID=@id",
                ("@id", id), ("@group", product.Group), ("@name", product.Name), ("@description", product.Description),
                ("@icon", product.Icon), ("@price", product.Price), ("@normal", product.NormalPrice), ("@order", product.Order), ("@enabled", product.Enabled));
            await Execute(connection, transaction, "DELETE FROM shop_product_action WHERE ProductID=@id", ("@id", id));
            for (int i = 0; i < product.Actions.Length; i++)
            {
                var action = product.Actions[i];
                await Execute(connection, transaction,
                    "INSERT INTO shop_product_action(ProductID,`Index`,Action,Param1,Param2) VALUES(@id,@index,@kind,@first,@second)",
                    ("@id", id), ("@index", i), ("@kind", action.Kind), ("@first", action.Param1), ("@second", action.Param2));
            }
            return id;
        });
        Log.outInfo(LogFilter.Sql, $"Support shop GM account {actor} saved product {id}, {product.Actions.Length} actions, price {product.Price}, enabled={product.Enabled}.");
        return id;
    }

    public static async Task<uint> SaveGroup(ShopDb db, SupportShopGroup group, uint actor)
    {
        ValidateText(group.Name, 127, "Group name");
        if (group.Id >= 100000000 || group.Icon < 0) throw new InvalidDataException("Invalid group ID or icon.");
        uint id = await Mutate(db, async (connection, transaction) =>
        {
            uint id = group.Id;
            if (id == 0)
            {
                id = Convert.ToUInt32(await Scalar(connection, transaction, "SELECT COALESCE(MAX(ID),0)+1 FROM shop_group WHERE ID<100000000"));
                if (id >= 100000000) throw new InvalidDataException("Group ID range exhausted.");
                await Execute(connection, transaction, "INSERT INTO shop_group(ID,Name) VALUES(@id,'')", ("@id", id));
            }
            else if (await Scalar(connection, transaction, "SELECT ID FROM shop_group WHERE ID=@id FOR UPDATE", ("@id", id)) == null)
                throw new InvalidDataException("This group no longer exists. Reload the catalog.");
            await Execute(connection, transaction, "UPDATE shop_group SET Name=@name,IconFileDataID=@icon,Ordering=@order WHERE ID=@id",
                ("@id", id), ("@name", group.Name), ("@icon", group.Icon), ("@order", group.Order));
            return id;
        });
        Log.outInfo(LogFilter.Sql, $"Support shop GM account {actor} saved group {id}.");
        return id;
    }

    public static async Task Delete(ShopDb db, uint id, bool group, uint actor)
    {
        if (id == 0 || id >= 100000000) throw new InvalidDataException("Invalid catalog ID.");
        await Mutate(db, async (connection, transaction) =>
        {
            if (group)
            {
                if (Convert.ToInt32(await Scalar(connection, transaction, "SELECT COUNT(*) FROM shop_product WHERE GroupID=@id", ("@id", id))) != 0)
                    throw new InvalidDataException("Move or remove this group's products before removing the group.");
                await Execute(connection, transaction, "DELETE FROM shop_group WHERE ID=@id", ("@id", id));
            }
            else
            {
                await Execute(connection, transaction, "DELETE FROM shop_product_action WHERE ProductID=@id", ("@id", id));
                await Execute(connection, transaction, "DELETE FROM shop_product WHERE ID=@id", ("@id", id));
            }
            return id;
        });
        Log.outInfo(LogFilter.Sql, $"Support shop GM account {actor} removed {(group ? "group" : "product")} {id}.");
    }

    static void ValidateText(string value, int maxBytes, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || Encoding.UTF8.GetByteCount(value) > maxBytes || value.Any(char.IsControl))
            throw new InvalidDataException($"{label} must contain 1..{maxBytes} UTF-8 bytes without control characters.");
    }

    static async Task<uint> Mutate(ShopDb db, Func<MySqlConnection, MySqlTransaction, Task<uint>> operation)
    {
        await using var connection = db.Connect();
        await connection.OpenAsync();
        await Ensure(connection);
        await using var acquire = new MySqlCommand("SELECT GET_LOCK('ForeverWorld.shop-catalog',5)", connection);
        if (Convert.ToInt32(await acquire.ExecuteScalarAsync()) != 1) throw new InvalidDataException("Catalog is busy. Try again.");
        try
        {
            await using var transaction = await connection.BeginTransactionAsync();
            uint id = await operation(connection, transaction);
            await transaction.CommitAsync();
            db.InvalidateShopCatalog();
            return id;
        }
        finally
        {
            await using var release = new MySqlCommand("SELECT RELEASE_LOCK('ForeverWorld.shop-catalog')", connection);
            await release.ExecuteScalarAsync();
        }
    }

    static MySqlCommand Command(MySqlConnection connection, MySqlTransaction transaction, string sql, (string, object)[] values)
    {
        var command = new MySqlCommand(sql, connection, transaction);
        foreach (var pair in values) command.Parameters.AddWithValue(pair.Item1, pair.Item2);
        return command;
    }
    static async Task<int> Execute(MySqlConnection connection, MySqlTransaction transaction, string sql, params (string, object)[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        return await command.ExecuteNonQueryAsync();
    }
    static async Task<object?> Scalar(MySqlConnection connection, MySqlTransaction transaction, string sql, params (string, object)[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        return await command.ExecuteScalarAsync();
    }
}
