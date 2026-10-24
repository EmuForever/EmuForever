using MySqlConnector;

namespace BattleNot;

// Battle.net and game account login: SRP credentials, login tickets, bans and account creation.
sealed class AuthDb
{
    readonly BnotDb _db;

    public AuthDb(BnotDb db) => _db = db;

    public async Task<bool> IsIpBanned(string ip)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT 1 FROM ip_banned WHERE ip = @ip AND (unbandate = bandate OR unbandate > UNIX_TIMESTAMP()) LIMIT 1", conn);
        cmd.Parameters.AddWithValue("@ip", ip);
        return await cmd.ExecuteScalarAsync() != null;
    }

    public async Task ExpireBans()
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "DELETE FROM ip_banned WHERE unbandate<>bandate AND unbandate<=UNIX_TIMESTAMP();" +
            "UPDATE account_banned SET active = 0 WHERE active = 1 AND unbandate<>bandate AND unbandate<=UNIX_TIMESTAMP();" +
            "DELETE FROM bnot_account_bans WHERE unbandate<>bandate AND unbandate<=UNIX_TIMESTAMP();", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(int Version, byte[] Salt, byte[] Verifier)?> PasswordByEmail(string email)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT srp_version, COALESCE(salt, 0x0000000000000000000000000000000000000000000000000000000000000000), verifier FROM bnot_accounts WHERE email = @email", conn);
        cmd.Parameters.AddWithValue("@email", email);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync() || reader.IsDBNull(2))
            return null;
        return (reader.GetInt32(0), (byte[])reader[1], (byte[])reader[2]);
    }

    public async Task<AuthRow?> Authentication(string email)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT ba.id, ba.srp_version, COALESCE(ba.salt, 0x0000000000000000000000000000000000000000000000000000000000000000), ba.verifier, ba.failed_logins, ba.LoginTicket, ba.LoginTicketExpiry, bab.unbandate > UNIX_TIMESTAMP() OR bab.unbandate = bab.bandate, ba.authenticator_secret FROM bnot_accounts ba LEFT JOIN bnot_account_bans bab ON ba.id = bab.id WHERE email = @email", conn);
        cmd.Parameters.AddWithValue("@email", email);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        return new AuthRow
        {
            Id = reader.GetUInt32(0),
            Version = reader.GetInt32(1),
            Salt = (byte[])reader[2],
            Verifier = reader.IsDBNull(3) ? [] : (byte[])reader[3],
            FailedLogins = reader.GetUInt32(4),
            Ticket = reader.IsDBNull(5) ? "" : reader.GetString(5),
            TicketExpiry = reader.IsDBNull(6) ? 0 : reader.GetUInt32(6),
            Banned = !reader.IsDBNull(7) && ToBool(reader.GetValue(7)),
            AuthenticatorSecret = reader.IsDBNull(8) ? "" : reader.GetString(8)
        };
    }

    public async Task SetTicket(uint id, string ticket, uint expiry)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE bnot_accounts SET LoginTicket = @ticket, LoginTicketExpiry = @expiry WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@ticket", ticket);
        cmd.Parameters.AddWithValue("@expiry", expiry);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<uint?> TicketExpiry(string ticket)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT LoginTicketExpiry FROM bnot_accounts WHERE LoginTicket = @ticket", conn);
        cmd.Parameters.AddWithValue("@ticket", ticket);
        object? value = await cmd.ExecuteScalarAsync();
        return value == null || value is DBNull ? null : Convert.ToUInt32(value);
    }

    public async Task RefreshTicket(string ticket, uint expiry)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE bnot_accounts SET LoginTicketExpiry = @expiry WHERE LoginTicket = @ticket", conn);
        cmd.Parameters.AddWithValue("@expiry", expiry);
        cmd.Parameters.AddWithValue("@ticket", ticket);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> TicketById(uint id)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT LoginTicket FROM bnot_accounts WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        object? value = await cmd.ExecuteScalarAsync();
        return value == null || value is DBNull ? null : Convert.ToString(value);
    }

    public async Task NoteFailedLogin(uint id)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE bnot_accounts SET failed_logins = failed_logins + 1 WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ResetFailedLogins(uint id)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE bnot_accounts SET failed_logins = 0 WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task BanAccount(uint id, int seconds)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("INSERT INTO bnot_account_bans(id, bandate, unbandate, bannedby, banreason) VALUES(@id, UNIX_TIMESTAMP(), UNIX_TIMESTAMP()+@seconds, 'Trinity Auth', 'Failed login autoban')", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@seconds", seconds);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task BanIp(string ip, int seconds)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("INSERT INTO ip_banned (ip, bandate, unbandate, bannedby, banreason) VALUES (@ip, UNIX_TIMESTAMP(), UNIX_TIMESTAMP()+@seconds, 'Trinity Auth', 'Failed login autoban')", conn);
        cmd.Parameters.AddWithValue("@ip", ip);
        cmd.Parameters.AddWithValue("@seconds", seconds);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<(string Name, byte Expansion, bool Suspended, bool Banned, string Reason, ulong Expires)>> GameAccounts(string ticket)
    {
        var list = new List<(string, byte, bool, bool, string, ulong)>();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT a.username, a.expansion, ab.bandate, ab.unbandate, ab.banreason FROM account AS a LEFT JOIN account_banned AS ab ON a.id = ab.id AND ab.active = 1 INNER JOIN bnot_accounts AS ba ON a.battlenet_account = ba.id WHERE ba.LoginTicket = @ticket ORDER BY a.id", conn);
        cmd.Parameters.AddWithValue("@ticket", ticket);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        while (await reader.ReadAsync())
        {
            bool hasBan = !reader.IsDBNull(2);
            uint ban = hasBan ? reader.GetUInt32(2) : 0;
            uint unban = hasBan && !reader.IsDBNull(3) ? reader.GetUInt32(3) : 0;
            list.Add((
                reader.GetString(0),
                reader.GetByte(1),
                hasBan && unban > now,
                hasBan && ban == unban,
                hasBan && !reader.IsDBNull(4) ? reader.GetString(4) : "",
                unban));
        }
        return list;
    }

    public async Task<BnetAccount?> AccountByTicket(string ticket)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT ba.id, UPPER(ba.email), ba.locked, ba.lock_country, ba.last_ip, ba.LoginTicketExpiry, " +
            "bab.unbandate > UNIX_TIMESTAMP() OR bab.unbandate = bab.bandate, bab.unbandate = bab.bandate, " +
            "a.id, a.username, ab.bandate, ab.unbandate, ab.unbandate = ab.bandate, aa.SecurityLevel " +
            "FROM bnot_accounts ba LEFT JOIN bnot_account_bans bab ON ba.id = bab.id " +
            "LEFT JOIN account a ON ba.id = a.battlenet_account " +
            "LEFT JOIN account_banned ab ON a.id = ab.id AND ab.active = 1 " +
            "LEFT JOIN account_access aa ON a.id = aa.AccountID AND aa.RealmID = -1 " +
            "WHERE ba.LoginTicket = @ticket ORDER BY a.id", conn);
        cmd.Parameters.AddWithValue("@ticket", ticket);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        BnetAccount? account = null;
        while (await reader.ReadAsync())
        {
            account ??= new BnetAccount
            {
                Id = reader.GetUInt32(0),
                Email = reader.IsDBNull(1) ? "" : reader.GetString(1),
                LockedToIp = !reader.IsDBNull(2) && ToBool(reader.GetValue(2)),
                LockCountry = reader.IsDBNull(3) ? "" : reader.GetString(3),
                LastIp = reader.IsDBNull(4) ? "" : reader.GetString(4),
                TicketExpiry = reader.IsDBNull(5) ? 0 : reader.GetUInt32(5),
                Banned = !reader.IsDBNull(6) && ToBool(reader.GetValue(6)),
                PermanentlyBanned = !reader.IsDBNull(7) && ToBool(reader.GetValue(7)),
                Ticket = ticket
            };
            if (reader.IsDBNull(8))
                continue;
            uint gameId = reader.GetUInt32(8);
            if (account.Games.Any(g => g.Id == gameId))
                continue;
            string name = reader.IsDBNull(9) ? "" : reader.GetString(9);
            int hash = name.IndexOf('#');
            account.Games.Add(new GameAccount
            {
                Id = gameId,
                Name = name,
                DisplayName = hash >= 0 ? "WoW" + name[(hash + 1)..] : name,
                BanDate = reader.IsDBNull(10) ? 0 : reader.GetUInt32(10),
                UnbanDate = reader.IsDBNull(11) ? 0 : reader.GetUInt32(11),
                PermanentlyBanned = !reader.IsDBNull(12) && ToBool(reader.GetValue(12)),
                Banned = !reader.IsDBNull(11) && reader.GetUInt32(11) > (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Security = reader.IsDBNull(13) ? (byte)0 : Convert.ToByte(reader.GetValue(13))
            });
        }
        if (account == null)
            return null;

        await reader.CloseAsync();
        await LoadCharacters(conn, account);
        return account;
    }

    async Task LoadCharacters(MySqlConnection conn, BnetAccount account)
    {
        await using (var cmd = new MySqlCommand(
            "SELECT rc.acctid, rc.numchars, r.id, r.Region, r.Battlegroup FROM realmcharacters rc INNER JOIN realmlist r ON rc.realmid = r.id LEFT JOIN account a ON rc.acctid = a.id WHERE a.battlenet_account = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", account.Id);
            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                GameAccount? game = account.Games.FirstOrDefault(g => g.Id == reader.GetUInt32(0));
                if (game == null)
                    continue;
                uint realmId = reader.GetUInt32(2);
                byte region = reader.GetByte(3);
                byte group = reader.GetByte(4);
                uint address = ((uint)region << 24) | ((uint)group << 16) | (ushort)realmId;
                game.CharacterCounts[address] = reader.GetByte(1);
            }
        }
        await using (var cmd = new MySqlCommand(
            "SELECT lpc.accountId, lpc.region, lpc.battlegroup, lpc.realmId, lpc.characterName, lpc.characterGUID, lpc.lastPlayedTime FROM account_last_played_character lpc LEFT JOIN account a ON lpc.accountId = a.id WHERE a.battlenet_account = @id", conn))
        {
            cmd.Parameters.AddWithValue("@id", account.Id);
            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                GameAccount? game = account.Games.FirstOrDefault(g => g.Id == reader.GetUInt32(0));
                if (game == null)
                    continue;
                byte region = reader.GetByte(1);
                byte group = reader.GetByte(2);
                uint realmId = reader.GetUInt32(3);
                string sub = $"{region}-{group}-{realmId}";
                game.LastPlayed[sub] = new LastPlayed
                {
                    RealmAddress = ((uint)region << 24) | ((uint)group << 16) | (ushort)realmId,
                    SubRegion = sub,
                    Name = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Guid = reader.IsDBNull(5) ? 0 : reader.GetUInt64(5),
                    Time = reader.IsDBNull(6) ? 0 : reader.GetUInt32(6)
                };
            }
        }
    }

    public async Task<string?> CreateAccount(string username, string email, string password)
    {
        username = Text.UpperLatin((username ?? "").Trim());
        email = Text.UpperLatin((email ?? "").Trim());
        if (!ValidAccountName(username))
            return "username";
        if (!ValidEmail(email))
            return "email";
        if (string.IsNullOrEmpty(password) || password.Length > 128)
            return "password";

        byte[] bnetSalt = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        byte[] bnetVerifier = BnetSrp.Verifier(2, Text.SrpUsername(email), password, bnetSalt);
        string gamePass = Text.UpperLatin(password);
        if (gamePass.Length > 16)
            gamePass = gamePass.Substring(0, 16);
        (byte[] gameSalt, byte[] gameVerifier) = GameSrp.Make(username, gamePass);

        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var accountLock = await Framework.Database.MySqlNamedLock.AcquireAsync(conn, "game-account-create");
        await Framework.Database.AccountIdAllocator.EnsureAsync(conn);
        await using MySqlTransaction tx = await conn.BeginTransactionAsync();
        try
        {
            if (await Scalar(conn, tx, "SELECT 1 FROM bnot_accounts WHERE email = @v LIMIT 1", email) != null)
            {
                await tx.RollbackAsync();
                return "email_exists";
            }
            if (await Scalar(conn, tx, "SELECT 1 FROM account WHERE username = @v LIMIT 1", username) != null)
            {
                await tx.RollbackAsync();
                return "username_exists";
            }

            await using (var insert = new MySqlCommand(
                "INSERT INTO bnot_accounts (email, srp_version, salt, verifier) VALUES (@email, 2, @salt, @verifier)", conn, tx))
            {
                insert.Parameters.AddWithValue("@email", email);
                insert.Parameters.AddWithValue("@salt", bnetSalt);
                insert.Parameters.AddWithValue("@verifier", bnetVerifier);
                await insert.ExecuteNonQueryAsync();
            }
            uint bnetId = Convert.ToUInt32(await Scalar(conn, tx, "SELECT LAST_INSERT_ID()", null));

            uint accountId = await Framework.Database.AccountIdAllocator.ReserveAsync(conn,tx);
            await using (var insert = new MySqlCommand(
                "INSERT INTO account (id, username, salt, verifier, reg_mail, email, joindate, battlenet_account, battlenet_index, expansion) VALUES (@accountId, @user, @salt, @verifier, @email, @email, NOW(), @bnet, 1, 0)", conn, tx))
            {
                insert.Parameters.AddWithValue("@accountId", accountId);
                insert.Parameters.AddWithValue("@user", username);
                insert.Parameters.AddWithValue("@salt", gameSalt);
                insert.Parameters.AddWithValue("@verifier", gameVerifier);
                insert.Parameters.AddWithValue("@email", email);
                insert.Parameters.AddWithValue("@bnet", bnetId);
                await insert.ExecuteNonQueryAsync();
            }
            await using (var realms = new MySqlCommand(
                "INSERT INTO realmcharacters (realmid, acctid, numchars) SELECT id, @acct, 0 FROM realmlist", conn, tx))
            {
                realms.Parameters.AddWithValue("@acct", accountId);
                await realms.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            return null;
        }
        catch (MySqlException ex)
        {
            try { await tx.RollbackAsync(); } catch { }
            if (ex.Number == 1062)
                return "username_exists";
            Log.outError(LogFilter.Sql, "Account create failed: " + ex.Message);
            return "fail";
        }
    }

    static async Task<object?> Scalar(MySqlConnection conn, MySqlTransaction tx, string sql, string? value)
    {
        await using var cmd = new MySqlCommand(sql, conn, tx);
        if (value != null)
            cmd.Parameters.AddWithValue("@v", value);
        object? found = await cmd.ExecuteScalarAsync();
        return found == null || found is DBNull ? null : found;
    }

    static bool ValidAccountName(string name)
    {
        if (name.Length < 2 || name.Length > 16)
            return false;
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c >= 'A' && c <= 'Z') continue;
            if (c >= '0' && c <= '9') continue;
            if (c == '_') continue;
            return false;
        }
        return true;
    }

    static bool ValidEmail(string email)
    {
        if (email.Length < 3 || email.Length > 320 || email.IndexOf(' ') >= 0)
            return false;
        int at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@'))
            return false;
        int dot = email.LastIndexOf('.');
        return dot > at + 1 && dot < email.Length - 1;
    }

    public async Task UpdateLastLogin(uint bnetId, string ip, byte locale, string os)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE bnot_accounts SET last_ip = @ip, last_login = NOW(), locale = @locale, failed_logins = 0, os = @os WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@ip", ip);
        cmd.Parameters.AddWithValue("@locale", locale);
        cmd.Parameters.AddWithValue("@os", os);
        cmd.Parameters.AddWithValue("@id", bnetId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateGameLogin(string username, byte[] sessionKey, string ip, uint build, byte locale, string os, short timezone)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE account SET session_key_bnet = @key, last_ip = @ip, last_login = NOW(), client_build = @build, locale = @locale, failed_logins = 0, os = @os, timezone_offset = @tz WHERE username = @name", conn);
        cmd.Parameters.AddWithValue("@key", sessionKey);
        cmd.Parameters.AddWithValue("@ip", ip);
        cmd.Parameters.AddWithValue("@build", build);
        cmd.Parameters.AddWithValue("@locale", locale);
        cmd.Parameters.AddWithValue("@os", os);
        cmd.Parameters.AddWithValue("@tz", timezone);
        cmd.Parameters.AddWithValue("@name", username);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(uint Id, string Email)?> SocialAccount(string ticket)
    {
        BnetAccount? account = await AccountByTicket(ticket);
        if (account == null || account.Banned)
            return null;
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (account.TicketExpiry < now)
            return null;
        return (account.Id, account.Email);
    }

    static bool ToBool(object value) => value switch
    {
        bool b => b,
        byte n => n != 0,
        sbyte n => n != 0,
        short n => n != 0,
        ushort n => n != 0,
        int n => n != 0,
        uint n => n != 0,
        long n => n != 0,
        ulong n => n != 0,
        _ => Convert.ToInt64(value) != 0
    };
}
