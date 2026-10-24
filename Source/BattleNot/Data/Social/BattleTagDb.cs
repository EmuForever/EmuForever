using System.Text;
using MySqlConnector;

namespace BattleNot;

readonly record struct BnetPerson(uint Id, string Tag, string Email);

// bnot_accounts.battle_tag: assigning, reading and looking accounts up by tag.
sealed class BattleTagDb
{
    readonly BnotDb _db;

    public BattleTagDb(BnotDb db) => _db = db;

    public async Task<string> EnsureTag(uint id, string email)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        return await EnsureTag(conn, id, email);
    }

    public async Task<string> BattleTagOf(uint id)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT IFNULL(battle_tag,'') FROM bnot_accounts WHERE id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        object? value = await cmd.ExecuteScalarAsync();
        return value == null || value is DBNull ? "" : value.ToString() ?? "";
    }

    public static async Task<string> EnsureTag(MySqlConnection conn, uint id, string email)
    {
        await using var existing = new MySqlCommand("SELECT IFNULL(battle_tag,'') FROM bnot_accounts WHERE id=@id", conn);
        existing.Parameters.AddWithValue("@id", id);
        object? value = await existing.ExecuteScalarAsync();
        string tag = value == null || value is DBNull ? "" : value.ToString() ?? "";
        if (tag.Length == 0)
        {
            string stem = TagStem(email);
            tag = stem + "#" + (id % 10000).ToString("D4");
            await using var taken = new MySqlCommand("SELECT id FROM bnot_accounts WHERE battle_tag=@tag", conn);
            taken.Parameters.AddWithValue("@tag", tag);
            object? owner = await taken.ExecuteScalarAsync();
            if (owner != null && owner is not DBNull)
                tag = stem + "#" + id.ToString();
            await WriteTag(conn, id, tag);
        }
        await using var presence = new MySqlCommand("INSERT IGNORE INTO bnot_social (account) VALUES (@id)", conn);
        presence.Parameters.AddWithValue("@id", id);
        await presence.ExecuteNonQueryAsync();
        return tag;
    }

    static string TagStem(string email)
    {
        int at = email.IndexOf('@');
        string local = at > 0 ? email[..at] : email;
        var stem = new StringBuilder();
        foreach (char c in local.ToUpperInvariant())
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
                stem.Append(c);
        if (stem.Length == 0)
            stem.Append("PLAYER");
        if (stem.Length > 12)
            stem.Length = 12;
        return stem.ToString();
    }

    public async Task<uint?> AccountByTag(string tag)
    {
        tag = (tag ?? "").Trim().ToUpperInvariant();
        if (tag.Length == 0)
            return null;
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT id FROM bnot_accounts WHERE UPPER(battle_tag)=@tag", conn);
        cmd.Parameters.AddWithValue("@tag", tag);
        object? value = await cmd.ExecuteScalarAsync();
        if (value == null || value is DBNull)
            return null;
        return Convert.ToUInt32(value);
    }

    public async Task<List<uint>> AccountsByTagStem(string stem)
    {
        var matches = new List<uint>();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id FROM bnot_accounts WHERE battle_tag IS NOT NULL AND battle_tag<>'' " +
            "AND UPPER(SUBSTRING_INDEX(battle_tag, '#', 1))=@stem ORDER BY id LIMIT 2", conn);
        cmd.Parameters.AddWithValue("@stem", stem.Trim().ToUpperInvariant());
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            matches.Add(reader.GetUInt32(0));
        return matches;
    }

    public async Task<string> GameBattleTag(uint id, string email)
    {
        await _db.Schema.EnsureBnetIdentity();
        string stored = await StoredTag(id);
        if (stored.Length > 0)
            return stored;
        if (email.Length == 0)
            email = await EmailOf(id);
        if (email.Length == 0)
            return "";
        string tag = await EnsureTag(id, email);
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await WriteTag(conn, id, tag);
        stored = await StoredTag(id);
        return stored.Length > 0 ? stored : tag;
    }

    public async Task<string> StoredTag(uint id)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT IFNULL(battle_tag,'') FROM bnot_accounts WHERE id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        object? value = await cmd.ExecuteScalarAsync();
        return value == null || value is DBNull ? "" : value.ToString() ?? "";
    }

    async Task<string> EmailOf(uint id)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT email FROM bnot_accounts WHERE id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        object? value = await cmd.ExecuteScalarAsync();
        return value == null || value is DBNull ? "" : value.ToString() ?? "";
    }

    public static async Task WriteTag(MySqlConnection conn, uint id, string tag)
    {
        if (tag.Length == 0)
            return;
        try
        {
            await using var cmd = new MySqlCommand(
                "UPDATE bnot_accounts SET battle_tag=@tag WHERE id=@id AND (battle_tag IS NULL OR battle_tag='')", conn);
            cmd.Parameters.AddWithValue("@tag", tag);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex)
        {
            Log.outError(LogFilter.Sql, "battle_tag " + tag + " " + ex.Message);
        }
    }

    public async Task<BnetPerson?> FindBnetPerson(ulong accountId, string tag, string email, string name)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        MySqlCommand cmd;
        if (accountId != 0)
        {
            cmd = new MySqlCommand("SELECT id, IFNULL(battle_tag,''), email FROM bnot_accounts WHERE id=@id", conn);
            cmd.Parameters.AddWithValue("@id", accountId);
        }
        else
        {
            string key = tag.Length > 0 ? tag : email.Length > 0 ? email : name;
            key = key.Trim().ToUpperInvariant();
            if (key.Length == 0)
                return null;
            cmd = new MySqlCommand(
                "SELECT id, IFNULL(battle_tag,''), email FROM bnot_accounts WHERE UPPER(battle_tag)=@key OR email=@key LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@key", key);
        }
        await using (cmd)
        await using (MySqlDataReader reader = await cmd.ExecuteReaderAsync())
        {
            if (!await reader.ReadAsync())
                return null;
            return new BnetPerson(reader.GetUInt32(0), reader.GetString(1), reader.GetString(2));
        }
    }
}
