using MySqlConnector;

namespace BattleNot;

readonly record struct ShopItem(uint ProductId, string SectionId, string SectionName, int SectionOrder, string Name, string Description, string ProductType, int LicenseId, string Currency, int PriceCents, int SortOrder);

// Storefront catalog read from the world database's shop_* tables.
sealed class ShopDb
{
    readonly BnotDb _db;
    readonly SemaphoreSlim _shopItemsGate = new(1, 1);
    ShopItem[]? _shopItemsCache;
    long _shopItemsExpiresAt;

    public ShopDb(BnotDb db) => _db = db;

    public MySqlConnection Connect() => _db.ConnectShop();
    public void InvalidateShopCatalog() => Volatile.Write(ref _shopItemsExpiresAt, 0);

    public async Task<List<ShopItem>> LoadShopItems(bool refresh = false)
    {
        ShopItem[]? cached = Volatile.Read(ref _shopItemsCache);
        if (!refresh && cached != null && Environment.TickCount64 < Volatile.Read(ref _shopItemsExpiresAt))
            return new List<ShopItem>(cached);

        await _shopItemsGate.WaitAsync();
        try
        {
            cached = Volatile.Read(ref _shopItemsCache);
            if (!refresh && cached != null && Environment.TickCount64 < Volatile.Read(ref _shopItemsExpiresAt))
                return new List<ShopItem>(cached);

            var items = new List<ShopItem>();
            await using MySqlConnection conn = Connect();
            await conn.OpenAsync();
            await SupportShopStore.Ensure(conn);
            await using var cmd = new MySqlCommand(
                "SELECT p.ID,p.GroupID,g.Name,g.Ordering,p.Name,p.Description,p.Price,p.Ordering,a.Action,a.Param1 FROM shop_product p JOIN shop_group g ON g.ID=p.GroupID LEFT JOIN shop_product_action a ON a.ProductID=p.ID AND a.`Index`=0 WHERE p.Enabled<>0 ORDER BY g.Ordering,g.ID,p.Ordering,p.ID", conn);
            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                byte action = reader.IsDBNull(8) ? (byte)0 : reader.GetByte(8);
                string type = action switch { 1 => "Item", 2 => "Gold", 3 => "Spell", 4 => "Service", 10 => "Boost", _ => "Product" };
                items.Add(new ShopItem(reader.GetUInt32(0), reader.GetUInt32(1).ToString(), reader.GetString(2),
                    reader.GetInt32(3), reader.GetString(4), reader.GetString(5), type,
                    reader.IsDBNull(9) ? 0 : reader.GetInt32(9), "POINTS",
                    (int)Math.Min(int.MaxValue, (ulong)reader.GetUInt32(6) * 100), reader.GetInt32(7)));
            }
            ShopItem[] snapshot = items.ToArray();
            Volatile.Write(ref _shopItemsCache, snapshot);
            Volatile.Write(ref _shopItemsExpiresAt, Environment.TickCount64 + 30_000);
            return items;
        }
        catch
        {
            ShopItem[]? stale = Volatile.Read(ref _shopItemsCache);
            if (stale == null)
                throw;
            // A transient database hiccup should not take an already-loaded storefront
            // offline. Refresh again after the short retry window below.
            Volatile.Write(ref _shopItemsExpiresAt, Environment.TickCount64 + 2_000);
            return new List<ShopItem>(stale);
        }
        finally
        {
            _shopItemsGate.Release();
        }
    }
}
