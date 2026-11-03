using MySqlConnector;
using System.Text.Json;

namespace BattleNot;

// This connection always targets the configured auth database, never the shop/world database.
sealed class ProductsDb(BnotDb db)
{
    readonly SemaphoreSlim _gate = new(1, 1);
    bool _ready;

    public async Task EnsureSchema()
    {
        await _gate.WaitAsync();
        try
        {
            if (_ready) return;
            await using var conn = db.Connect();
            await conn.OpenAsync();
            await using var exists = new MySqlCommand(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='bnot_products'", conn);
            bool tableExisted = Convert.ToInt64(await exists.ExecuteScalarAsync()) > 0;
            await using (var create = new MySqlCommand("""
                CREATE TABLE IF NOT EXISTS bnot_products (
                    id VARCHAR(64) NOT NULL PRIMARY KEY,
                    name VARCHAR(200) NOT NULL,
                    family VARCHAR(100) NOT NULL DEFAULT 'World of Warcraft',
                    enabled TINYINT UNSIGNED NOT NULL DEFAULT 1,
                    sort_order INT NOT NULL DEFAULT 0,
                    distribution_json JSON NOT NULL,
                    client_json JSON NOT NULL,
                    services_json JSON NOT NULL,
                    updates_json JSON NOT NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
                """, conn))
                await create.ExecuteNonQueryAsync();
            await using var transaction = await conn.BeginTransactionAsync();
            await using var count = new MySqlCommand("SELECT COUNT(*) FROM bnot_products", conn, transaction);
            if (!tableExisted && Convert.ToInt64(await count.ExecuteScalarAsync()) == 0)
            {
                string file = Path.Combine(AppContext.BaseDirectory, "products.json");
                var catalog = ProductCatalogJson.Parse(await File.ReadAllTextAsync(file));
                for (int index = 0; index < catalog.Products.Count; index++)
                {
                    var product = catalog.Products[index];
                    await using var insert = new MySqlCommand("""
                        INSERT IGNORE INTO bnot_products
                          (id, name, family, enabled, sort_order, distribution_json, client_json, services_json, updates_json)
                        VALUES (@id, @name, @family, @enabled, @sort, @distribution, @client, @services, @updates)
                        """, conn, transaction);
                    insert.Parameters.AddWithValue("@id", product.Id);
                    insert.Parameters.AddWithValue("@name", product.Name);
                    insert.Parameters.AddWithValue("@family", product.Family);
                    insert.Parameters.AddWithValue("@enabled", product.Enabled);
                    insert.Parameters.AddWithValue("@sort", index);
                    insert.Parameters.AddWithValue("@distribution", JsonSerializer.Serialize(product.Distribution, ProductCatalogJson.Options));
                    insert.Parameters.AddWithValue("@client", JsonSerializer.Serialize(product.Client, ProductCatalogJson.Options));
                    insert.Parameters.AddWithValue("@services", JsonSerializer.Serialize(product.Services, ProductCatalogJson.Options));
                    insert.Parameters.AddWithValue("@updates", JsonSerializer.Serialize(product.Updates, ProductCatalogJson.Options));
                    await insert.ExecuteNonQueryAsync();
                }
            }
            await transaction.CommitAsync();
            _ready = true;
        }
        finally { _gate.Release(); }
    }

    public async Task<BnotProductCatalog> Load()
    {
        await EnsureSchema();
        await using var conn = db.Connect();
        await conn.OpenAsync();
        await using var query = new MySqlCommand("""
            SELECT id, name, family, enabled, distribution_json, client_json, services_json, updates_json
            FROM bnot_products ORDER BY sort_order, id
            """, conn);
        await using var reader = await query.ExecuteReaderAsync();
        var catalog = new BnotProductCatalog();
        while (await reader.ReadAsync())
            catalog.Products.Add(new BnotProduct {
                Id = reader.GetString(0), Name = reader.GetString(1), Family = reader.GetString(2), Enabled = reader.GetBoolean(3),
                Distribution = Read<ProductDistribution>(reader, 4), Client = Read<ProductClient>(reader, 5),
                Services = Read<ProductServices>(reader, 6), Updates = Read<ProductUpdates>(reader, 7)
            });
        ProductCatalogJson.Validate(catalog);
        return catalog;
    }

    static T Read<T>(MySqlDataReader reader, int column) =>
        JsonSerializer.Deserialize<T>(reader.GetString(column), ProductCatalogJson.Options)
        ?? throw new InvalidDataException("Invalid product configuration in bnot_products.");
}
