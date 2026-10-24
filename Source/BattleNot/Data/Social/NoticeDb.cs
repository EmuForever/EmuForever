using MySqlConnector;

namespace BattleNot;

// bnot_notice: the launcher's notification feed.
sealed class NoticeDb
{
    readonly BnotDb _db;

    public NoticeDb(BnotDb db) => _db = db;

    public async Task AddNotice(uint account, string kind, string text)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO bnot_notice (account, kind, text, sent, unread) VALUES (@account, @kind, @text, @sent, 1)", conn);
        cmd.Parameters.AddWithValue("@account", account);
        cmd.Parameters.AddWithValue("@kind", DbValue.Clip(kind, 32));
        cmd.Parameters.AddWithValue("@text", DbValue.Clip(text, 200));
        cmd.Parameters.AddWithValue("@sent", now);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<(ulong Id, string Kind, string Text, bool Unread)>> Notices(uint id)
    {
        await _db.Schema.EnsureBnetIdentity();
        var list = new List<(ulong, string, string, bool)>();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id, kind, text, unread FROM bnot_notice WHERE account=@id ORDER BY id DESC LIMIT 30", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add((reader.GetUInt64(0), reader.GetString(1), reader.GetString(2), reader.GetByte(3) != 0));
        return list;
    }

    public async Task ReadNotices(uint id)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE bnot_notice SET unread=0 WHERE account=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
