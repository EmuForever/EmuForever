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

    public async Task<List<(uint Id, string Name, byte Expansion, bool Suspended, bool Banned, string Reason, ulong Expires)>?> GameAccounts(string ticket)
    {
        await _db.Schema.EnsureGameAccountNames();
        var list = new List<(uint, string, byte, bool, bool, string, ulong)>();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        if (await GameAccountOwner(conn, null, ticket) == null) return null;
        await using var cmd = new MySqlCommand(
            "SELECT a.bnot_name_key, a.expansion, ab.bandate, ab.unbandate, ab.banreason, a.id FROM account AS a LEFT JOIN account_banned AS ab ON a.id = ab.id AND ab.active = 1 INNER JOIN bnot_accounts AS ba ON a.battlenet_account = ba.id WHERE ba.LoginTicket = @ticket ORDER BY a.id", conn);
        cmd.Parameters.AddWithValue("@ticket", ticket);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        while (await reader.ReadAsync())
        {
            bool hasBan = !reader.IsDBNull(2);
            uint ban = hasBan ? reader.GetUInt32(2) : 0;
            uint unban = hasBan && !reader.IsDBNull(3) ? reader.GetUInt32(3) : 0;
            list.Add((
                reader.GetUInt32(5),
                reader.GetString(0),
                reader.GetByte(1),
                hasBan && unban > now,
                hasBan && ban == unban,
                hasBan && !reader.IsDBNull(4) ? reader.GetString(4) : "",
                unban));
        }
        return list;
    }

    static async Task<(uint Id, string Email, bool Locked, string Ip)?> GameAccountOwner(MySqlConnection conn, MySqlTransaction? tx, string ticket)
    {
        await using var cmd = new MySqlCommand(
            "SELECT ba.id, ba.email, ba.locked, ba.last_ip FROM bnot_accounts ba " +
            "WHERE ba.LoginTicket=@ticket AND ba.LoginTicketExpiry>UNIX_TIMESTAMP() " +
            "AND NOT EXISTS (SELECT 1 FROM bnot_account_bans b WHERE b.id=ba.id AND (b.unbandate=b.bandate OR b.unbandate>UNIX_TIMESTAMP())) " +
            "LIMIT 1" + (tx == null ? "" : " FOR UPDATE"),conn,tx);
        cmd.Parameters.AddWithValue("@ticket",ticket);
        await using var reader=await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? (reader.GetUInt32(0),reader.GetString(1),reader.GetBoolean(2),reader.IsDBNull(3) ? "" : reader.GetString(3)) : null;
    }

    public async Task<(uint Id, string Name, string? Error)> CreateGameAccount(string ticket, string remoteIp, string? requestedName = null)
    {
        await _db.Schema.EnsureGameAccountNames();
        string? customName = requestedName == null ? null : Text.UpperLatin(requestedName.Trim());
        if (customName != null && !ValidAccountName(customName)) return (0,"","username");
        await using var conn=_db.Connect(); await conn.OpenAsync();
        var preliminary=await GameAccountOwner(conn,null,ticket);
        if (preliminary == null) return (0,"","unauthorized");
        await using var accountLock=await Framework.Database.MySqlNamedLock.AcquireAsync(conn,"game-account-create");
        await Framework.Database.AccountIdAllocator.EnsureAsync(conn);
        await using var tx=await conn.BeginTransactionAsync();
        try {
            var owner=await GameAccountOwner(conn,tx,ticket);
            if (owner == null) return (0,"","unauthorized");
            if (owner.Value.Locked && !SameIp(owner.Value.Ip,remoteIp)) return (0,"","locked");
            int index;
            await using (var maximum=new MySqlCommand("SELECT COALESCE(MAX(battlenet_index),0) FROM account WHERE battlenet_account=@owner",conn,tx)) {
                maximum.Parameters.AddWithValue("@owner",owner.Value.Id); index=Convert.ToInt32(await maximum.ExecuteScalarAsync())+1;
            }
            if (customName != null) {
                await using var duplicate = new MySqlCommand("SELECT 1 FROM account WHERE battlenet_account=@owner AND bnot_name_key=@name LIMIT 1",conn,tx);
                duplicate.Parameters.AddWithValue("@owner",owner.Value.Id); duplicate.Parameters.AddWithValue("@name",customName);
                if (await duplicate.ExecuteScalarAsync()!=null) return (0,"","username_exists");
            }
            string name="";
            for (;index<=byte.MaxValue;index++) {
                name=owner.Value.Id+"#"+index;
                if (await Scalar(conn,tx,"SELECT 1 FROM account WHERE username=@v LIMIT 1",name)==null) break;
            }
            if (index>byte.MaxValue) return (0,"","limit");
            // Match core gameaccountcreate: BNet-only account with random GRUNT credentials.
            var (salt,verifier)=GameSrp.Make(name,Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8)));
            uint id=await Framework.Database.AccountIdAllocator.ReserveAsync(conn,tx);
            await using (var insert=new MySqlCommand(
                "INSERT INTO account(id,username,bnot_name,salt,verifier,reg_mail,email,joindate,battlenet_account,battlenet_index) " +
                "VALUES(@id,@name,@publicName,@salt,@verifier,@email,@email,NOW(),@owner,@index)",conn,tx)) {
                insert.Parameters.AddWithValue("@id",id); insert.Parameters.AddWithValue("@name",name);
                insert.Parameters.AddWithValue("@publicName",(object?)customName ?? DBNull.Value);
                insert.Parameters.AddWithValue("@salt",salt); insert.Parameters.AddWithValue("@verifier",verifier);
                insert.Parameters.AddWithValue("@email",owner.Value.Email); insert.Parameters.AddWithValue("@owner",owner.Value.Id);
                insert.Parameters.AddWithValue("@index",index); await insert.ExecuteNonQueryAsync();
            }
            await using (var realms=new MySqlCommand("INSERT INTO realmcharacters(realmid,acctid,numchars) SELECT id,@id,0 FROM realmlist",conn,tx)) {
                realms.Parameters.AddWithValue("@id",id); await realms.ExecuteNonQueryAsync();
            }
            await tx.CommitAsync();
            Log.outDebug(LogFilter.Http,$"Game account {id} created for BattleNot account {owner.Value.Id}.");
            return (id,customName ?? name,null);
        }
        catch (MySqlException ex) {
            try { await tx.RollbackAsync(); } catch { }
            Log.outError(LogFilter.Sql,"Game account creation failed: "+ex.Number);
            return (0,"","failed");
        }
    }

    static bool SameIp(string left,string right) => System.Net.IPAddress.TryParse(left,out var a) && System.Net.IPAddress.TryParse(right,out var b) && a.MapToIPv6().Equals(b.MapToIPv6());

    public async Task<BnetAccount?> AccountByTicket(string ticket)
    {
        await _db.Schema.EnsureGameAccountNames();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT ba.id, UPPER(ba.email), ba.locked, ba.lock_country, ba.last_ip, ba.LoginTicketExpiry, " +
            "bab.unbandate > UNIX_TIMESTAMP() OR bab.unbandate = bab.bandate, bab.unbandate = bab.bandate, " +
            "a.id, a.username, ab.bandate, ab.unbandate, ab.unbandate = ab.bandate, aa.SecurityLevel, a.bnot_name_key " +
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
            account.Games.Add(new GameAccount
            {
                Id = gameId,
                Name = name,
                AccountName = reader.IsDBNull(14) ? name : reader.GetString(14),
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
        await _db.Schema.EnsureGameAccountNames();
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


        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var accountLock = await Framework.Database.MySqlNamedLock.AcquireAsync(conn, "game-account-create");
        await using var bnetAccountLock = await Framework.Database.MySqlNamedLock.AcquireAsync(conn, "bnet-account-create");
        await Framework.Database.AccountIdAllocator.EnsureAsync(conn);
        await Framework.Database.AccountIdAllocator.EnsureAsync(conn, "bnot_accounts", "bnot_account_id_allocator");
        await using MySqlTransaction tx = await conn.BeginTransactionAsync();
        try
        {
            if (await Scalar(conn, tx, "SELECT 1 FROM bnot_accounts WHERE email = @v LIMIT 1", email) != null)
            {
                await tx.RollbackAsync();
                return "email_exists";
            }

            uint bnetId = await Framework.Database.AccountIdAllocator.ReserveAsync(conn, tx, "bnot_accounts", "bnot_account_id_allocator");
            await using (var insert = new MySqlCommand(
                "INSERT INTO bnot_accounts (id, email, srp_version, salt, verifier) VALUES (@id, @email, 2, @salt, @verifier)", conn, tx))
            {
                insert.Parameters.AddWithValue("@id", bnetId);
                insert.Parameters.AddWithValue("@email", email);
                insert.Parameters.AddWithValue("@salt", bnetSalt);
                insert.Parameters.AddWithValue("@verifier", bnetVerifier);
                await insert.ExecuteNonQueryAsync();
            }

            int gameIndex=1;
            string loginName="";
            for (;gameIndex<=byte.MaxValue;gameIndex++) {
                loginName=bnetId+"#"+gameIndex;
                if (await Scalar(conn,tx,"SELECT 1 FROM account WHERE username=@v LIMIT 1",loginName)==null) break;
            }
            if (gameIndex>byte.MaxValue) { await tx.RollbackAsync(); return "fail"; }
            (byte[] gameSalt, byte[] gameVerifier) = GameSrp.Make(loginName, gamePass);
            uint accountId = await Framework.Database.AccountIdAllocator.ReserveAsync(conn,tx);
            await using (var insert = new MySqlCommand(
                "INSERT INTO account (id, username, bnot_name, salt, verifier, reg_mail, email, joindate, battlenet_account, battlenet_index, expansion) VALUES (@accountId, @user, @publicName, @salt, @verifier, @email, @email, NOW(), @bnet, @gameIndex, 0)", conn, tx))
            {
                insert.Parameters.AddWithValue("@accountId", accountId);
                insert.Parameters.AddWithValue("@user", loginName);
                insert.Parameters.AddWithValue("@publicName", username);
                insert.Parameters.AddWithValue("@gameIndex", gameIndex);
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

    public async Task UpdateGameLogin(uint accountId, byte[] sessionKey, string ip, uint build, byte locale, string os, short timezone)
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE account SET session_key_bnet = @key, last_ip = @ip, last_login = NOW(), client_build = @build, locale = @locale, failed_logins = 0, os = @os, timezone_offset = @tz WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("@key", sessionKey);
        cmd.Parameters.AddWithValue("@ip", ip);
        cmd.Parameters.AddWithValue("@build", build);
        cmd.Parameters.AddWithValue("@locale", locale);
        cmd.Parameters.AddWithValue("@os", os);
        cmd.Parameters.AddWithValue("@tz", timezone);
        cmd.Parameters.AddWithValue("@id", accountId);
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
