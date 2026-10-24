using MySqlConnector;

namespace BattleNot;

// Creates and migrates the bnot_* tables and columns on first use.
sealed class BnetSchema
{
    readonly BnotDb _db;

    public BnetSchema(BnotDb db) => _db = db;

    int _bnetReady;
    readonly SemaphoreSlim _bnetGate = new(1, 1);

    public async Task EnsureBnetIdentity()
    {
        if (Volatile.Read(ref _bnetReady) == 1)
            return;
        await _bnetGate.WaitAsync();
        try
        {
            if (_bnetReady == 1)
                return;
            await EnsureSocialTables();
            await using MySqlConnection conn = _db.Connect();
            await conn.OpenAsync();
            if (await ColumnMissing(conn, "bnot_accounts", "battle_tag"))
            {
                await using var alter = new MySqlCommand(
                    "ALTER TABLE bnot_accounts ADD COLUMN battle_tag VARCHAR(32) NULL DEFAULT NULL AFTER email", conn);
                await alter.ExecuteNonQueryAsync();
                Log.outInfo(LogFilter.Sql, "bnot_accounts.battle_tag added");
            }
            if (await ColumnMissing(conn, "bnot_accounts", "authenticator_secret"))
            {
                await using var alter = new MySqlCommand(
                    "ALTER TABLE bnot_accounts ADD COLUMN authenticator_secret VARCHAR(64) NULL DEFAULT NULL AFTER LoginTicketExpiry", conn);
                await alter.ExecuteNonQueryAsync();
                Log.outInfo(LogFilter.Sql, "bnot_accounts.authenticator_secret added");
            }
            await using (var friends = new MySqlCommand(
                "CREATE TABLE IF NOT EXISTS bnot_friends (" +
                "account_id INT UNSIGNED NOT NULL," +
                "friend_account_id INT UNSIGNED NOT NULL," +
                "created INT UNSIGNED NOT NULL DEFAULT 0," +
                "note VARCHAR(127) NOT NULL DEFAULT ''," +
                "PRIMARY KEY (account_id, friend_account_id)," +
                "KEY idx_friend (friend_account_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn))
                await friends.ExecuteNonQueryAsync();
            await using (var invites = new MySqlCommand(
                "CREATE TABLE IF NOT EXISTS bnot_friend_invitations (" +
                "id INT UNSIGNED NOT NULL AUTO_INCREMENT," +
                "inviter_account_id INT UNSIGNED NOT NULL," +
                "invitee_account_id INT UNSIGNED NOT NULL," +
                "created INT UNSIGNED NOT NULL DEFAULT 0," +
                "PRIMARY KEY (id)," +
                "UNIQUE KEY uk_pair (inviter_account_id, invitee_account_id)," +
                "KEY idx_invitee (invitee_account_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn))
                await invites.ExecuteNonQueryAsync();
            if (await ColumnMissing(conn, "bnot_friends", "favorite"))
            {
                await using var favorite = new MySqlCommand(
                    "ALTER TABLE bnot_friends ADD COLUMN favorite TINYINT UNSIGNED NOT NULL DEFAULT 0", conn);
                await favorite.ExecuteNonQueryAsync();
            }
            await using (var blocks = new MySqlCommand(
                "CREATE TABLE IF NOT EXISTS bnot_blocks (" +
                "account_id INT UNSIGNED NOT NULL," +
                "blocked_account_id INT UNSIGNED NOT NULL," +
                "PRIMARY KEY (account_id, blocked_account_id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", conn))
                await blocks.ExecuteNonQueryAsync();
            if (!await ColumnMissing(conn, "bnot_social", "battletag"))
            {
                await using (var copy = new MySqlCommand(
                    "SELECT s.account, s.battletag FROM bnot_social s INNER JOIN bnot_accounts ba ON ba.id = s.account " +
                    "WHERE ba.battle_tag IS NULL OR ba.battle_tag = ''", conn))
                await using (MySqlDataReader reader = await copy.ExecuteReaderAsync())
                {
                    var rows = new List<(uint Id, string Tag)>();
                    while (await reader.ReadAsync())
                        rows.Add((reader.GetUInt32(0), reader.GetString(1)));
                    await reader.CloseAsync();
                    foreach ((uint id, string tag) in rows)
                        await BattleTagDb.WriteTag(conn, id, tag);
                }
                await using var dropTag = new MySqlCommand("ALTER TABLE bnot_social DROP COLUMN battletag", conn);
                await dropTag.ExecuteNonQueryAsync();
                Log.outInfo(LogFilter.Sql, "bnot_social.battletag removed; tags are bnot_accounts.battle_tag.");
            }
            if (await IndexMissing(conn, "bnot_accounts", "uk_battle_tag"))
            {
                await using var index = new MySqlCommand(
                    "ALTER TABLE bnot_accounts ADD UNIQUE KEY uk_battle_tag (battle_tag)", conn);
                await index.ExecuteNonQueryAsync();
            }
            if (await TableExists(conn, "bnet_friend"))
            {
                await using (var moveFriends = new MySqlCommand(
                    "INSERT IGNORE INTO bnot_friends (account_id, friend_account_id, created, note, favorite) " +
                    "SELECT owner, friend, UNIX_TIMESTAMP(), '', bnet_friend.favorite FROM bnet_friend WHERE state=2", conn))
                    await moveFriends.ExecuteNonQueryAsync();
                await using (var moveOut = new MySqlCommand(
                    "INSERT IGNORE INTO bnot_friend_invitations (inviter_account_id, invitee_account_id, created) " +
                    "SELECT owner, friend, UNIX_TIMESTAMP() FROM bnet_friend WHERE state=0", conn))
                    await moveOut.ExecuteNonQueryAsync();
                await using (var moveIn = new MySqlCommand(
                    "INSERT IGNORE INTO bnot_friend_invitations (inviter_account_id, invitee_account_id, created) " +
                    "SELECT friend, owner, UNIX_TIMESTAMP() FROM bnet_friend WHERE state=1", conn))
                    await moveIn.ExecuteNonQueryAsync();
                await using (var moveBlocks = new MySqlCommand(
                    "INSERT IGNORE INTO bnot_blocks (account_id, blocked_account_id) " +
                    "SELECT owner, friend FROM bnet_friend WHERE state=3", conn))
                    await moveBlocks.ExecuteNonQueryAsync();
                await using var dropFriends = new MySqlCommand("DROP TABLE bnet_friend", conn);
                await dropFriends.ExecuteNonQueryAsync();
                Log.outInfo(LogFilter.Sql, "bnet_friend removed; friends are bnot_friends.");
            }
            Volatile.Write(ref _bnetReady, 1);
            Log.outInfo(LogFilter.Sql, "Battle.net friends and authenticator database schema ready.");
        }
        finally
        {
            _bnetGate.Release();
        }
    }

    int _socialReady;

    async Task EnsureSocialTables()
    {
        if (Volatile.Read(ref _socialReady) == 1)
            return;
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using (var social = new MySqlCommand(
            "CREATE TABLE IF NOT EXISTS bnot_social (" +
            "account INT UNSIGNED NOT NULL PRIMARY KEY," +
            "status VARCHAR(16) NOT NULL DEFAULT 'offline'," +
            "seen INT UNSIGNED NOT NULL DEFAULT 0," +
            "game VARCHAR(64) NOT NULL DEFAULT ''," +
            "character_name VARCHAR(16) NOT NULL DEFAULT ''," +
            "realm_name VARCHAR(64) NOT NULL DEFAULT ''," +
            "bio VARCHAR(200) NOT NULL DEFAULT ''," +
            "visibility TINYINT UNSIGNED NOT NULL DEFAULT 0) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            conn))
            await social.ExecuteNonQueryAsync();
        await using (var bio = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bnot_social' AND COLUMN_NAME = 'bio'",
            conn))
        {
            long hasBio = Convert.ToInt64(await bio.ExecuteScalarAsync() ?? 0);
            if (hasBio == 0)
            {
                await using var alter = new MySqlCommand(
                    "ALTER TABLE bnot_social ADD COLUMN bio VARCHAR(200) NOT NULL DEFAULT '', ADD COLUMN visibility TINYINT UNSIGNED NOT NULL DEFAULT 0",
                    conn);
                await alter.ExecuteNonQueryAsync();
                Log.outInfo(LogFilter.Sql, "bnot_social.bio added");
            }
        }
        foreach (string sql in new[]
        {
            "CREATE TABLE IF NOT EXISTS bnot_message (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, sender INT UNSIGNED NOT NULL, recipient INT UNSIGNED NOT NULL, body VARCHAR(500) NOT NULL, sent INT UNSIGNED NOT NULL, seen TINYINT UNSIGNED NOT NULL DEFAULT 0, KEY inbox (recipient, id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "CREATE TABLE IF NOT EXISTS bnot_notice (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, account INT UNSIGNED NOT NULL, kind VARCHAR(32) NOT NULL, text VARCHAR(200) NOT NULL, sent INT UNSIGNED NOT NULL, unread TINYINT UNSIGNED NOT NULL DEFAULT 1, KEY account (account, id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "CREATE TABLE IF NOT EXISTS bnot_gift (id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, sender INT UNSIGNED NOT NULL, recipient INT UNSIGNED NOT NULL, product_id INT UNSIGNED NOT NULL, note VARCHAR(200) NOT NULL DEFAULT '', sent INT UNSIGNED NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "CREATE TABLE IF NOT EXISTS bnot_group (id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, name VARCHAR(32) NOT NULL, owner INT UNSIGNED NOT NULL) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "CREATE TABLE IF NOT EXISTS bnot_group_member (group_id INT UNSIGNED NOT NULL, account INT UNSIGNED NOT NULL, PRIMARY KEY (group_id, account)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4",
            "CREATE TABLE IF NOT EXISTS bnot_group_message (id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY, group_id INT UNSIGNED NOT NULL, sender INT UNSIGNED NOT NULL, body VARCHAR(500) NOT NULL, sent INT UNSIGNED NOT NULL, KEY room (group_id, id)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"
        })
        {
            await using var table = new MySqlCommand(sql, conn);
            await table.ExecuteNonQueryAsync();
        }
        Volatile.Write(ref _socialReady, 1);
    }

    static async Task<bool> TableExists(MySqlConnection conn, string table)
    {
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table", conn);
        cmd.Parameters.AddWithValue("@table", table);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0) != 0;
    }

    static async Task<bool> ColumnMissing(MySqlConnection conn, string table, string column)
    {
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column", conn);
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@column", column);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0) == 0;
    }

    static async Task<bool> IndexMissing(MySqlConnection conn, string table, string index)
    {
        await using var cmd = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND INDEX_NAME = @index", conn);
        cmd.Parameters.AddWithValue("@table", table);
        cmd.Parameters.AddWithValue("@index", index);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0) == 0;
    }
}
