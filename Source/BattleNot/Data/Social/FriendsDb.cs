using MySqlConnector;

namespace BattleNot;

// bnot_friends, bnot_friend_invitations and bnot_blocks.
sealed class FriendsDb
{
    readonly BnotDb _db;

    public FriendsDb(BnotDb db) => _db = db;

    public async Task<List<(string Tag, string Status, string Game, string Character, string Realm, bool Favorite, byte State)>> FriendList(uint id)
    {
        await _db.Schema.EnsureBnetIdentity();
        var list = new List<(string, string, string, string, string, bool, byte)>();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT tag, status, seen, game, character_name, realm_name, favorite, state FROM (" +
            "SELECT a.battle_tag AS tag, IFNULL(s.status,'offline') AS status, IFNULL(s.seen,0) AS seen, IFNULL(s.game,'') AS game, " +
            "IFNULL(s.character_name,'') AS character_name, IFNULL(s.realm_name,'') AS realm_name, f.favorite AS favorite, 2 AS state " +
            "FROM bnot_friends f INNER JOIN bnot_accounts a ON a.id = f.friend_account_id " +
            "LEFT JOIN bnot_social s ON s.account = f.friend_account_id WHERE f.account_id=@id " +
            "UNION ALL " +
            "SELECT a.battle_tag, 'offline', 0, '', '', '', 0, 0 FROM bnot_friend_invitations i " +
            "INNER JOIN bnot_accounts a ON a.id = i.invitee_account_id WHERE i.inviter_account_id=@id " +
            "UNION ALL " +
            "SELECT a.battle_tag, 'offline', 0, '', '', '', 0, 1 FROM bnot_friend_invitations i " +
            "INNER JOIN bnot_accounts a ON a.id = i.inviter_account_id WHERE i.invitee_account_id=@id " +
            "UNION ALL " +
            "SELECT a.battle_tag, 'offline', 0, '', '', '', 0, 3 FROM bnot_blocks b " +
            "INNER JOIN bnot_accounts a ON a.id = b.blocked_account_id WHERE b.account_id=@id" +
            ") friends WHERE tag IS NOT NULL AND tag <> '' ORDER BY favorite DESC, tag", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            string status = reader.GetString(1);
            uint seen = Convert.ToUInt32(reader.GetValue(2));
            if (status == "hidden" || status == "offline" || seen == 0 || seen + 120 < now)
                status = "offline";
            list.Add((
                reader.GetString(0),
                status,
                status == "offline" ? "" : reader.GetString(3),
                status == "offline" ? "" : reader.GetString(4),
                status == "offline" ? "" : reader.GetString(5),
                Convert.ToInt32(reader.GetValue(6)) != 0,
                Convert.ToByte(reader.GetValue(7))));
        }
        return list;
    }

    public async Task<string> AddFriend(uint id, string tag)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null && !string.IsNullOrWhiteSpace(tag) && !tag.Contains('#'))
        {
            List<uint> matches = await _db.Tags.AccountsByTagStem(tag);
            if (matches.Count > 1)
                return "ambiguous";
            if (matches.Count == 1)
                other = matches[0];
        }
        if (other == null)
            return "missing";
        if (other.Value == id)
            return "self";
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        byte? mine = await FriendState(conn, id, other.Value);
        byte? theirs = await FriendState(conn, other.Value, id);
        if (mine == 3 || theirs == 3)
            return "blocked";
        if (mine == 2)
            return "ok";
        if (mine == 1)
        {
            await AcceptGameFriends(other.Value, id, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return "ok";
        }
        await InsertInvite(id, other.Value, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        string mineTag = await _db.Tags.BattleTagOf(id);
        await _db.Notices.AddNotice(other.Value, "friend_request", mineTag);
        return "ok";
    }

    public async Task<string> AcceptFriend(uint id, string tag)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null)
            return "missing";
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        byte? mine = await FriendState(conn, id, other.Value);
        if (mine != 1)
            return "missing";
        await AcceptGameFriends(other.Value, id, (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return "ok";
    }

    public async Task RemoveFriend(uint id, string tag)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null)
            return;
        await DropGameFriends(id, other.Value);
    }

    public async Task<string> RejectFriend(uint id, string tag)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null)
            return "missing";
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        byte? mine = await FriendState(conn, id, other.Value);
        if (mine != 1)
            return "missing";
        await DeleteInvitePair(other.Value, id);
        return "ok";
    }

    public async Task<string> BlockFriend(uint id, string tag)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null)
            return "missing";
        if (other.Value == id)
            return "self";
        await DropGameFriends(id, other.Value);
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var block = new MySqlCommand(
            "INSERT IGNORE INTO bnot_blocks (account_id, blocked_account_id) VALUES (@a, @b)", conn);
        block.Parameters.AddWithValue("@a", id);
        block.Parameters.AddWithValue("@b", other.Value);
        await block.ExecuteNonQueryAsync();
        Log.outInfo(LogFilter.Sql, "Blocked " + tag + " for account " + id);
        return "ok";
    }

    public async Task UnblockFriend(uint id, string tag)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null)
            return;
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM bnot_blocks WHERE account_id=@a AND blocked_account_id=@b", conn);
        cmd.Parameters.AddWithValue("@a", id);
        cmd.Parameters.AddWithValue("@b", other.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task FavoriteFriend(uint id, string tag, bool favorite)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint? other = await _db.Tags.AccountByTag(tag);
        if (other == null)
            return;
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE bnot_friends SET favorite=@favorite WHERE account_id=@owner AND friend_account_id=@friend", conn);
        cmd.Parameters.AddWithValue("@favorite", favorite ? 1 : 0);
        cmd.Parameters.AddWithValue("@owner", id);
        cmd.Parameters.AddWithValue("@friend", other.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> AreFriends(uint a, uint b)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        byte? state = await FriendState(conn, a, b);
        return state == 2;
    }

    public async Task<bool> LauncherBlocked(uint owner, uint other)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        byte? state = await FriendState(conn, owner, other);
        return state == 3;
    }

    public async Task<ulong> InsertInvite(uint inviter, uint invitee, uint created)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "INSERT IGNORE INTO bnot_friend_invitations (inviter_account_id, invitee_account_id, created) VALUES (@a, @b, @created)", conn);
        cmd.Parameters.AddWithValue("@a", inviter);
        cmd.Parameters.AddWithValue("@b", invitee);
        cmd.Parameters.AddWithValue("@created", created);
        await cmd.ExecuteNonQueryAsync();
        if (cmd.LastInsertedId > 0)
            return (ulong)cmd.LastInsertedId;
        await using var read = new MySqlCommand(
            "SELECT id FROM bnot_friend_invitations WHERE inviter_account_id=@a AND invitee_account_id=@b", conn);
        read.Parameters.AddWithValue("@a", inviter);
        read.Parameters.AddWithValue("@b", invitee);
        object? value = await read.ExecuteScalarAsync();
        return value == null || value is DBNull ? 0 : Convert.ToUInt64(value);
    }

    public async Task<uint?> InviteInviter(ulong invitationId, uint invitee)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT inviter_account_id FROM bnot_friend_invitations WHERE id=@id AND invitee_account_id=@invitee", conn);
        cmd.Parameters.AddWithValue("@id", invitationId);
        cmd.Parameters.AddWithValue("@invitee", invitee);
        object? value = await cmd.ExecuteScalarAsync();
        return value == null || value is DBNull ? null : Convert.ToUInt32(value);
    }

    public async Task<(uint Inviter, uint Invitee)?> InvitePair(ulong invitationId, uint me, bool asInvitee)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        string column = asInvitee ? "invitee_account_id" : "inviter_account_id";
        await using var cmd = new MySqlCommand(
            "SELECT inviter_account_id, invitee_account_id FROM bnot_friend_invitations WHERE id=@id AND " + column + "=@me", conn);
        cmd.Parameters.AddWithValue("@id", invitationId);
        cmd.Parameters.AddWithValue("@me", me);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return (reader.GetUInt32(0), reader.GetUInt32(1));
    }

    public async Task DeleteInvite(ulong invitationId)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("DELETE FROM bnot_friend_invitations WHERE id=@id", conn);
        cmd.Parameters.AddWithValue("@id", invitationId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteInvitePair(uint inviter, uint invitee)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM bnot_friend_invitations WHERE inviter_account_id=@a AND invitee_account_id=@b", conn);
        cmd.Parameters.AddWithValue("@a", inviter);
        cmd.Parameters.AddWithValue("@b", invitee);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<ulong>> SentInviteIds(uint inviter)
    {
        var ids = new List<ulong>();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT id FROM bnot_friend_invitations WHERE inviter_account_id=@id", conn);
        cmd.Parameters.AddWithValue("@id", inviter);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetUInt64(0));
        return ids;
    }

    public async Task AcceptGameFriends(uint inviter, uint invitee, uint created)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using (var drop = new MySqlCommand(
            "DELETE FROM bnot_friend_invitations WHERE (inviter_account_id=@a AND invitee_account_id=@b) OR (inviter_account_id=@b AND invitee_account_id=@a)", conn))
        {
            drop.Parameters.AddWithValue("@a", inviter);
            drop.Parameters.AddWithValue("@b", invitee);
            await drop.ExecuteNonQueryAsync();
        }
        await using var add = new MySqlCommand(
            "INSERT INTO bnot_friends (account_id, friend_account_id, created) VALUES (@a, @b, @created), (@b, @a, @created) " +
            "ON DUPLICATE KEY UPDATE created=created", conn);
        add.Parameters.AddWithValue("@a", inviter);
        add.Parameters.AddWithValue("@b", invitee);
        add.Parameters.AddWithValue("@created", created);
        await add.ExecuteNonQueryAsync();
    }

    public async Task DropGameFriends(uint a, uint b)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using (var friends = new MySqlCommand(
            "DELETE FROM bnot_friends WHERE (account_id=@a AND friend_account_id=@b) OR (account_id=@b AND friend_account_id=@a)", conn))
        {
            friends.Parameters.AddWithValue("@a", a);
            friends.Parameters.AddWithValue("@b", b);
            await friends.ExecuteNonQueryAsync();
        }
        await using var invites = new MySqlCommand(
            "DELETE FROM bnot_friend_invitations WHERE (inviter_account_id=@a AND invitee_account_id=@b) OR (inviter_account_id=@b AND invitee_account_id=@a)", conn);
        invites.Parameters.AddWithValue("@a", a);
        invites.Parameters.AddWithValue("@b", b);
        await invites.ExecuteNonQueryAsync();
    }

    public async Task SetFriendNote(uint owner, uint friend, string note)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE bnot_friends SET note=@note WHERE account_id=@a AND friend_account_id=@b", conn);
        cmd.Parameters.AddWithValue("@note", note);
        cmd.Parameters.AddWithValue("@a", owner);
        cmd.Parameters.AddWithValue("@b", friend);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<byte?> FriendState(MySqlConnection conn, uint owner, uint friend)
    {
        if (await PairExists(conn, "SELECT 1 FROM bnot_blocks WHERE account_id=@a AND blocked_account_id=@b", owner, friend))
            return 3;
        if (await PairExists(conn, "SELECT 1 FROM bnot_friends WHERE account_id=@a AND friend_account_id=@b", owner, friend))
            return 2;
        if (await PairExists(conn, "SELECT 1 FROM bnot_friend_invitations WHERE inviter_account_id=@a AND invitee_account_id=@b", owner, friend))
            return 0;
        if (await PairExists(conn, "SELECT 1 FROM bnot_friend_invitations WHERE inviter_account_id=@b AND invitee_account_id=@a", owner, friend))
            return 1;
        return null;
    }

    static async Task<bool> PairExists(MySqlConnection conn, string sql, uint a, uint b)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@a", a);
        cmd.Parameters.AddWithValue("@b", b);
        return await cmd.ExecuteScalarAsync() != null;
    }
}
