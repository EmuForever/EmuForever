using MySqlConnector;
using Framework.Configuration;
using Framework.Constants;
using System.Text.Json;

namespace BattleNot;

sealed record ProfileCharacter(string Name, int Level, string Guild, int Achievements, string Realm,
    uint Health, uint Power, uint MaxHealth, uint MaxPower, uint ZoneId, string Zone, bool IsDead);

// bnot_social: presence, bio and visibility, plus the character list shown on a profile.
sealed class ProfileDb
{
    readonly BnotDb _db;
    static readonly Lazy<Dictionary<uint, string>> ZoneNames = new(LoadZoneNames);

    static Dictionary<uint, string> LoadZoneNames()
    {
        try
        {
            string file = ConfigMgr.GetDefaultValue("Profiles.ZoneNamesFile", "");
            using Stream source = file.Length > 0 ? File.OpenRead(file) : typeof(ProfileDb).Assembly.GetManifestResourceStream("BattleNot.ProfileZones.enUS.json")
                ?? throw new FileNotFoundException("Profile zone-name catalog is missing.");
            return JsonSerializer.Deserialize<Dictionary<uint, string>>(source) ?? [];
        }
        catch (Exception ex) { Log.outError(LogFilter.Sql, "Profile zone names: " + ex.Message); return []; }
    }

    internal static bool CharacterIsDead(uint health, uint flags) => health == 0 || (flags & (uint)PlayerFlags.Ghost) != 0;

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

    internal static List<(uint RealmId, string Database)> ParseCharacterDatabases(string text)
    {
        var mappings = new List<(uint RealmId, string Database)>();
        // Older configurations retain their existing character source without guessing its realm.
        if (string.IsNullOrWhiteSpace(text)) return [(0, "characters")];
        foreach (string entry in text.Split(','))
        {
            string[] parts = entry.Trim().Split(':');
            string database = parts.Length == 2 ? parts[1].Trim() : "";
            if (parts.Length != 2 || !uint.TryParse(parts[0].Trim(), out uint realmId) || realmId == 0 || realmId > ushort.MaxValue
                || database.Length == 0 || database.Length > 64 || database.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
                throw new InvalidDataException("Profiles.CharacterDatabases must contain realmId:database pairs.");
            if (mappings.Any(m => m.RealmId == realmId || string.Equals(m.Database, database, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Profiles.CharacterDatabases contains a duplicate realm or character database.");
            mappings.Add((realmId, database));
        }
        return mappings;
    }

    public async Task<List<ProfileCharacter>> CharactersOf(uint bnetId)
    {
        var list = new List<ProfileCharacter>();
        List<(uint RealmId, string Database)> mappings;
        try { mappings = ParseCharacterDatabases(ConfigMgr.GetDefaultValue("Profiles.CharacterDatabases", "")); }
        catch (Exception ex) { Log.outError(LogFilter.Sql, "Profile characters: " + ex.Message); return list; }
        foreach (var source in mappings)
        {
            try
            {
                await using MySqlConnection conn = _db.Connect();
                await conn.OpenAsync();
                // The schema identifier is validated above. Realm names come from auth.realmlist, not account history.
                string schema = "`" + source.Database + "`";
                await using var cmd = new MySqlCommand(
                    "SELECT CONCAT_WS(' ',c.name,NULLIF(TRIM(c.surname),'')), c.level, IFNULL(g.name, ''), " +
                    $"(SELECT COUNT(*) FROM {schema}.character_achievement ca WHERE ca.guid = c.guid), IFNULL(r.name, ''), " +
                    "c.health, c.power1, IFNULL(stats.maxhealth,0), IFNULL(stats.maxpower1,0), c.zone, c.playerFlags " +
                    $"FROM {schema}.characters c INNER JOIN account a ON a.id = c.account " +
                    $"LEFT JOIN {schema}.guild_member gm ON gm.guid = c.guid " +
                    $"LEFT JOIN {schema}.guild g ON g.guildid = gm.guildid " +
                    $"LEFT JOIN {schema}.character_stats stats ON stats.guid = c.guid " +
                    "LEFT JOIN realmlist r ON r.id = @realm " +
                    "WHERE a.battlenet_account = @id AND c.deleteDate IS NULL " +
                    "ORDER BY c.level DESC, c.name LIMIT 12", conn);
                cmd.Parameters.AddWithValue("@id", bnetId);
                cmd.Parameters.AddWithValue("@realm", source.RealmId);
                await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    uint health = reader.GetUInt32(5), zoneId = reader.GetUInt32(9);
                    ZoneNames.Value.TryGetValue(zoneId, out string? zone);
                    list.Add(new ProfileCharacter(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4),
                        health, reader.GetUInt32(6), reader.GetUInt32(7), reader.GetUInt32(8), zoneId, zone ?? "", CharacterIsDead(health, reader.GetUInt32(10))));
                }
            }
            catch (Exception ex) { Log.outError(LogFilter.Sql, $"Profile characters realm {source.RealmId}: {ex.Message}"); }
        }
        return list.OrderByDescending(c => c.Level).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Realm, StringComparer.OrdinalIgnoreCase).Take(12).ToList();
    }
}
