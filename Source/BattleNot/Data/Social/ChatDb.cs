using MySqlConnector;

namespace BattleNot;

// bnot_message (direct messages) and bnot_group* (group chat).
sealed class ChatDb
{
    readonly BnotDb _db;

    public ChatDb(BnotDb db) => _db = db;

    public async Task<string> SendMessage(uint id, string tag, string body)
    {
        await _db.Schema.EnsureBnetIdentity();
        body = DbValue.Clip(body, 500).Trim();
        if (body.Length == 0)
            return "empty";
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null)
            return "missing";
        if (other.Value == id)
            return "self";
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        byte? mine = await FriendsDb.FriendState(conn, id, other.Value);
        byte? theirs = await FriendsDb.FriendState(conn, other.Value, id);
        if (mine == 3 || theirs == 3)
            return "blocked";
        if (mine != 2 || theirs != 2)
            return "friend";
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var cmd = new MySqlCommand(
            "INSERT INTO bnot_message (sender, recipient, body, sent) VALUES (@sender, @recipient, @body, @sent)", conn);
        cmd.Parameters.AddWithValue("@sender", id);
        cmd.Parameters.AddWithValue("@recipient", other.Value);
        cmd.Parameters.AddWithValue("@body", body);
        cmd.Parameters.AddWithValue("@sent", now);
        await cmd.ExecuteNonQueryAsync();
        string mineTag = await _db.Tags.BattleTagOf(id);
        await _db.Notices.AddNotice(other.Value, "message", mineTag);
        return "ok";
    }

    public async Task<List<(ulong Id, string Tag, string Body, uint Sent, bool Mine)>> Messages(uint id, string tag)
    {
        var list = new List<(ulong, string, string, uint, bool)>();
        await _db.Schema.EnsureBnetIdentity();
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null)
            return list;
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT m.id, IFNULL(a.battle_tag,''), m.body, m.sent, m.sender FROM bnot_message m " +
            "INNER JOIN bnot_accounts a ON a.id = m.sender " +
            "WHERE (m.sender=@a AND m.recipient=@b) OR (m.sender=@b AND m.recipient=@a) " +
            "ORDER BY m.id DESC LIMIT 40", conn);
        cmd.Parameters.AddWithValue("@a", id);
        cmd.Parameters.AddWithValue("@b", other.Value);
        var rows = new List<(ulong, string, string, uint, bool)>();
        await using (MySqlDataReader reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                rows.Add((reader.GetUInt64(0), reader.GetString(1), reader.GetString(2), reader.GetUInt32(3), reader.GetUInt32(4) == id));
        }
        for (int i = rows.Count - 1; i >= 0; i--)
            list.Add(rows[i]);
        await using var seen = new MySqlCommand("UPDATE bnot_message SET seen=1 WHERE recipient=@a AND sender=@b", conn);
        seen.Parameters.AddWithValue("@a", id);
        seen.Parameters.AddWithValue("@b", other.Value);
        await seen.ExecuteNonQueryAsync();
        return list;
    }

    public async Task<uint> CreateGroup(uint id, string name)
    {
        await _db.Schema.EnsureBnetIdentity();
        name = DbValue.Clip(name, 32).Trim();
        if (name.Length == 0)
            return 0;
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("INSERT INTO bnot_group (name, owner) VALUES (@name, @owner)", conn);
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@owner", id);
        await cmd.ExecuteNonQueryAsync();
        uint group = (uint)cmd.LastInsertedId;
        await using var member = new MySqlCommand("INSERT INTO bnot_group_member (group_id, account) VALUES (@group, @account)", conn);
        member.Parameters.AddWithValue("@group", group);
        member.Parameters.AddWithValue("@account", id);
        await member.ExecuteNonQueryAsync();
        return group;
    }

    public async Task<string> InviteGroup(uint id, uint group, string tag)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null)
            return "missing";
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var owner = new MySqlCommand("SELECT owner FROM bnot_group WHERE id=@id", conn);
        owner.Parameters.AddWithValue("@id", group);
        object? who = await owner.ExecuteScalarAsync();
        if (who == null || who is DBNull || Convert.ToUInt32(who) != id)
            return "missing";
        if (!await _db.Friends.AreFriends(id, other.Value))
            return "friend";
        await using var cmd = new MySqlCommand(
            "INSERT IGNORE INTO bnot_group_member (group_id, account) VALUES (@group, @account)", conn);
        cmd.Parameters.AddWithValue("@group", group);
        cmd.Parameters.AddWithValue("@account", other.Value);
        await cmd.ExecuteNonQueryAsync();
        string mineTag = await _db.Tags.BattleTagOf(id);
        await _db.Notices.AddNotice(other.Value, "group", mineTag);
        return "ok";
    }

    public async Task<List<(uint Id, string Name)>> Groups(uint id)
    {
        await _db.Schema.EnsureBnetIdentity();
        var list = new List<(uint, string)>();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT g.id, g.name FROM bnot_group g INNER JOIN bnot_group_member m ON m.group_id = g.id WHERE m.account=@id ORDER BY g.name", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add((reader.GetUInt32(0), reader.GetString(1)));
        return list;
    }

    public async Task<string> SendGroup(uint id, uint group, string body)
    {
        await _db.Schema.EnsureBnetIdentity();
        body = DbValue.Clip(body, 500).Trim();
        if (body.Length == 0)
            return "empty";
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var member = new MySqlCommand("SELECT 1 FROM bnot_group_member WHERE group_id=@group AND account=@account", conn);
        member.Parameters.AddWithValue("@group", group);
        member.Parameters.AddWithValue("@account", id);
        object? value = await member.ExecuteScalarAsync();
        if (value == null || value is DBNull)
            return "missing";
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var cmd = new MySqlCommand(
            "INSERT INTO bnot_group_message (group_id, sender, body, sent) VALUES (@group, @sender, @body, @sent)", conn);
        cmd.Parameters.AddWithValue("@group", group);
        cmd.Parameters.AddWithValue("@sender", id);
        cmd.Parameters.AddWithValue("@body", body);
        cmd.Parameters.AddWithValue("@sent", now);
        await cmd.ExecuteNonQueryAsync();
        return "ok";
    }

    public async Task<List<(ulong Id, string Tag, string Body, uint Sent, bool Mine)>> GroupMessages(uint id, uint group)
    {
        var list = new List<(ulong, string, string, uint, bool)>();
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var member = new MySqlCommand("SELECT 1 FROM bnot_group_member WHERE group_id=@group AND account=@account", conn);
        member.Parameters.AddWithValue("@group", group);
        member.Parameters.AddWithValue("@account", id);
        object? value = await member.ExecuteScalarAsync();
        if (value == null || value is DBNull)
            return list;
        await using var cmd = new MySqlCommand(
            "SELECT m.id, IFNULL(a.battle_tag,''), m.body, m.sent, m.sender FROM bnot_group_message m " +
            "INNER JOIN bnot_accounts a ON a.id = m.sender WHERE m.group_id=@group ORDER BY m.id DESC LIMIT 40", conn);
        cmd.Parameters.AddWithValue("@group", group);
        var rows = new List<(ulong, string, string, uint, bool)>();
        await using (MySqlDataReader reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                rows.Add((reader.GetUInt64(0), reader.GetString(1), reader.GetString(2), reader.GetUInt32(3), reader.GetUInt32(4) == id));
        }
        for (int i = rows.Count - 1; i >= 0; i--)
            list.Add(rows[i]);
        return list;
    }
}
