using System.Net;
using MySqlConnector;

namespace BattleNot;

// realmlist and build_info.
sealed class RealmDb
{
    readonly BnotDb _db;

    public RealmDb(BnotDb db) => _db = db;

    public async Task<List<RealmEntry>> LoadRealms()
    {
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await EnsureHardcoreColumn(conn);
        await using var cmd = new MySqlCommand(
            "SELECT id, name, address, localAddress, address3, address4, port, icon, flag, timezone, allowedSecurityLevel, population, gamebuild, Region, Battlegroup, contentSetId, hardcore FROM realmlist WHERE flag <> 3 ORDER BY name",
            conn);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        var realms = new List<RealmEntry>();
        while (await reader.ReadAsync())
        {
            var realm = new RealmEntry
            {
                Id = reader.GetUInt32(0),
                Name = reader.GetString(1),
                Port = reader.GetUInt16(6),
                Icon = reader.GetByte(7),
                Type = reader.GetByte(7),
                LegacyFlags = reader.GetByte(8),
                Timezone = reader.GetByte(9),
                AllowedSecurity = reader.GetByte(10),
                Population = reader.IsDBNull(11) ? 0 : reader.GetFloat(11),
                Build = reader.GetUInt32(12),
                Region = reader.GetByte(13),
                Battlegroup = reader.GetByte(14),
                ContentSetId = reader.IsDBNull(15) ? 0 : reader.GetUInt32(15),
                Hardcore = !reader.IsDBNull(16) && reader.GetByte(16) != 0
            };
            if (realm.Type == 4)
                realm.Type = 1;
            if (realm.Type >= 14)
                realm.Type = 0;
            for (int i = 2; i <= 5; i++)
            {
                if (reader.IsDBNull(i))
                    continue;
                string text = reader.GetString(i);
                if (text.Length == 0)
                    continue;
                if (IPAddress.TryParse(text, out IPAddress? ip))
                {
                    if (!realm.Addresses.Contains(ip))
                        realm.Addresses.Add(ip);
                    continue;
                }
                try
                {
                    foreach (IPAddress resolved in Dns.GetHostAddresses(text))
                        if (!realm.Addresses.Contains(resolved))
                            realm.Addresses.Add(resolved);
                }
                catch (Exception ex)
                {
                    Log.outError(LogFilter.Sql, $"Could not resolve {text} for realm {realm.Name}: {ex.Message}");
                }
            }
            if (realm.Addresses.Count == 0)
            {
                Log.outInfo(LogFilter.Sql, $"Skipping realm {realm.Name}: no address.");
                continue;
            }
            realms.Add(realm);
        }
        return realms;
    }

    static async Task EnsureHardcoreColumn(MySqlConnection conn)
    {
        await using var check = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'realmlist' AND COLUMN_NAME = 'hardcore'",
            conn);
        if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0)
            return;
        await using var alter = new MySqlCommand(
            "ALTER TABLE realmlist ADD COLUMN hardcore TINYINT UNSIGNED NOT NULL DEFAULT 0", conn);
        await alter.ExecuteNonQueryAsync();
        await using var seed = new MySqlCommand(
            "UPDATE realmlist SET hardcore = 1 WHERE contentSetId = 140 OR name = 'Hardcore'", conn);
        await seed.ExecuteNonQueryAsync();
        Log.outInfo(LogFilter.Sql, "realmlist.hardcore added");
    }

    public async Task<Dictionary<uint, BuildInfo>> LoadBuilds()
    {
        var builds = new Dictionary<uint, BuildInfo>();
        await using MySqlConnection conn = _db.Connect();
        await conn.OpenAsync();
        await using var cmd = new MySqlCommand("SELECT majorVersion, minorVersion, bugfixVersion, build FROM build_info", conn);
        await using MySqlDataReader reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var info = new BuildInfo
            {
                Major = reader.GetUInt32(0),
                Minor = reader.GetUInt32(1),
                Revision = reader.GetUInt32(2),
                Build = reader.GetUInt32(3)
            };
            builds[info.Build] = info;
        }
        return builds;
    }
}
