using MySqlConnector;

namespace BattleNot;

// bnot_social: presence, bio and visibility, plus the character list shown on a profile.
sealed class ProfileDb
{
    readonly BnotDb _db;

    public ProfileDb(BnotDb db) => _db = db;

    public async Task<string> SetPresence(uint id, string email, string status, string game, string character, string realm)
    {
        await _db.Schema.EnsureBnetIdentity();
        if (status != "online" && status != "away" && status != "busy" && status != "offline" && status != "hidden")
            status = "online";
        game = DbValue.Clip(game, 64);
        character = DbValue.Clip(character, 16);
        realm = DbValue.Clip(realm, 64);
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        string tag = await BattleTagDb.EnsureTag(conn, id, email);
        string previous = "offline";
        uint previousSeen = 0;
        await using (var prev = new MySqlCommand("SELECT status, seen FROM bnot_social WHERE account=@id", conn))
        {
            prev.Parameters.AddWithValue("@id", id);
            await using MySqlDataReader reader = await prev.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                previous = reader.GetString(0);
                previousSeen = reader.GetUInt32(1);
            }
        }
        bool wasShown = Shown(previous) && previousSeen != 0 && previousSeen + 120 >= now;
        await using var update = new MySqlCommand(
            "UPDATE bnot_social SET status=@status, seen=@seen, game=@game, character_name=@character, realm_name=@realm WHERE account=@id",
            conn);
        update.Parameters.AddWithValue("@status", status);
        bool quiet = status == "offline" || status == "hidden";
        update.Parameters.AddWithValue("@seen", status == "offline" ? 0 : now);
        update.Parameters.AddWithValue("@game", quiet ? "" : game);
        update.Parameters.AddWithValue("@character", quiet ? "" : character);
        update.Parameters.AddWithValue("@realm", quiet ? "" : realm);
        update.Parameters.AddWithValue("@id", id);
        await update.ExecuteNonQueryAsync();
        await using var online = new MySqlCommand("UPDATE bnot_accounts SET online=@on WHERE id=@id", conn);
        online.Parameters.AddWithValue("@on", Shown(status) ? 1 : 0);
        online.Parameters.AddWithValue("@id", id);
        await online.ExecuteNonQueryAsync();
        if (!wasShown && Shown(status))
        {
            string kind = game.Length > 0 ? "friend_playing" : "friend_online";
            await using var note = new MySqlCommand(
                "INSERT INTO bnot_notice (account, kind, text, sent, unread) " +
                "SELECT account_id, @kind, @text, @sent, 1 FROM bnot_friends WHERE friend_account_id=@id", conn);
            note.Parameters.AddWithValue("@kind", kind);
            note.Parameters.AddWithValue("@text", tag);
            note.Parameters.AddWithValue("@sent", now);
            note.Parameters.AddWithValue("@id", id);
            await note.ExecuteNonQueryAsync();
        }
        return tag;
    }

    static bool Shown(string status) => status is "online" or "away" or "busy";

    public async Task<(string Tag, string Status, string Bio, int Visibility)?> OwnSocial(uint id, string email)
    {
        await _db.Schema.EnsureBnetIdentity();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        string tag = await BattleTagDb.EnsureTag(conn, id, email);
        await using var cmd = new MySqlCommand("SELECT status, bio, visibility FROM bnot_social WHERE account=@id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (tag, "offline", "", 0);
        return (tag, reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
    }

    public async Task SaveProfile(uint id, string bio, int visibility)
    {
        await _db.Schema.EnsureBnetIdentity();
        if (visibility < 0 || visibility > 2)
            visibility = 0;
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("UPDATE bnot_social SET bio=@bio, visibility=@visibility WHERE account=@id", conn);
        cmd.Parameters.AddWithValue("@bio", DbValue.Clip(bio, 200));
        cmd.Parameters.AddWithValue("@visibility", visibility);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(uint Id, string Tag, string Status, string Bio, int Visibility)?> SocialByTag(string tag)
    {
        await _db.Schema.EnsureBnetIdentity();
        uint? id = await _db.Tags.AccountByTag(tag);
        if (id == null)
            return null;
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand(
            "SELECT IFNULL(a.battle_tag,''), IFNULL(s.status,'offline'), IFNULL(s.seen,0), IFNULL(s.bio,''), IFNULL(s.visibility,0) " +
            "FROM bnot_accounts a LEFT JOIN bnot_social s ON s.account = a.id WHERE a.id=@id", conn);
        cmd.Parameters.AddWithValue("@id", id.Value);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;
        string status = reader.GetString(1);
        uint seen = Convert.ToUInt32(reader.GetValue(2));
        uint now = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (status == "hidden" || status == "offline" || seen == 0 || seen + 120 < now)
            status = "offline";
        return (id.Value, reader.GetString(0), status, reader.GetString(3), Convert.ToInt32(reader.GetValue(4)));
    }

    public async Task<List<(string Name, int Level, string Guild, int Achievements)>> CharactersOf(uint bnetId)
    {
        var list = new List<(string, int, string, int)>();
        try
        {
            await using MySqlConnection conn = _db.Connect();
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand(
                "SELECT c.name, c.level, IFNULL(g.name, ''), " +
                "(SELECT COUNT(*) FROM characters.character_achievement ca WHERE ca.guid = c.guid) " +
                "FROM characters.characters c INNER JOIN account a ON a.id = c.account " +
                "LEFT JOIN characters.guild_member gm ON gm.guid = c.guid " +
                "LEFT JOIN characters.guild g ON g.guildid = gm.guildid " +
                "WHERE a.battlenet_account = @id AND c.deleteDate IS NULL " +
                "ORDER BY c.level DESC, c.name LIMIT 12", conn);
            cmd.Parameters.AddWithValue("@id", bnetId);
            await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                list.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3)));
        }
        catch (Exception ex)
        {
            Log.outError(LogFilter.Sql, "Profile characters: " + ex.Message);
        }
        return list;
    }
}
