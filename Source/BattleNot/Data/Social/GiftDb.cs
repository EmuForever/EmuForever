using MySqlConnector;

namespace BattleNot;

// bnot_gift: shop products sent between friends.
sealed class GiftDb
{
    readonly BnotDb _db;

    public GiftDb(BnotDb db) => _db = db;

    public async Task<string> SendGift(uint id, string tag, uint productId, string note)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null)
            return "missing";
        if (other.Value == id)
            return "self";
        if (!await _db.Friends.AreFriends(id, other.Value))
            return "friend";
        string product = "";
        foreach (ShopItem item in await _db.Shop.LoadShopItems())
            if (item.ProductId == productId)
                product = item.Name;
        if (product.Length == 0)
            return "missing";
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO bnot_gift (sender, recipient, product_id, note, sent) VALUES (@sender, @recipient, @product, @note, @sent)", conn);
        cmd.Parameters.AddWithValue("@sender", id);
        cmd.Parameters.AddWithValue("@recipient", other.Value);
        cmd.Parameters.AddWithValue("@product", productId);
        cmd.Parameters.AddWithValue("@note", DbValue.Clip(note, 200));
        cmd.Parameters.AddWithValue("@sent", now);
        await cmd.ExecuteNonQueryAsync();
        string mineTag = await _db.Tags.BattleTagOf(id);
        await _db.Notices.AddNotice(other.Value, "gift", mineTag + " · " + product);
        Log.outInfo(LogFilter.Sql, "Gift " + product + " from account " + id + " to " + tag);
        return "ok";
    }
}
