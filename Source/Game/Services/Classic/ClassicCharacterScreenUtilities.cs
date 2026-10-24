using Framework.Constants;
using Framework.Database;
using Framework.IO;
using Framework.Realm;
using MySqlConnector;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Game.Services.Classic;

// The Classic glue screen calls GameUtilities V2 on its realm connection after
// disconnecting from the Battle.net front. V1 has different Variant field IDs.
internal static class ClassicCharacterScreenUtilities
{
    public const uint ServiceHash = 0x5DBB51C2;

    internal static (string Command, long ContentSet) ReadRequest(byte[] payload)
    {
        string command = string.Empty;
        long content = -1;
        foreach (var attribute in ProtoField.Walk(payload))
        {
            if (attribute.Field != 1 || attribute.Wire != 2) continue;
            string name = string.Empty;
            byte[] value = [];
            foreach (var field in ProtoField.Walk(attribute.Bytes))
            {
                if (field.Field == 1 && field.Wire == 2) name = Encoding.UTF8.GetString(field.Bytes);
                if (field.Field == 2 && field.Wire == 2) value = field.Bytes;
            }
            if (name.StartsWith("Command_", StringComparison.Ordinal))
            {
                int suffix = name.IndexOf("_v", StringComparison.Ordinal);
                command = suffix < 0 ? name : name[..suffix];
            }
            if (name == "Param_ContentSetIDFilter")
                foreach (var field in ProtoField.Walk(value))
                    if (field.Wire == 0 && (field.Field == 2 || field.Field == 6))
                        content = unchecked((long)field.Varint);
        }
        return (command, content);
    }

    public static async Task<BnetCall> Handle(uint method, byte[] payload)
    {
        var request = ReadRequest(payload);
        if (method != 1 || request.Command != "Command_LastCharPlayedRequest")
            return new BnetCall { Status = (uint)BattlenetRpcErrorCode.RpcNotImplemented, Command = request.Command };

        Realm local = Global.WorldMgr.GetRealm();
        await using var connection = DB.Login.CreateConnection();
        await connection.OpenAsync();
        await using var command = new MySqlCommand(
            "SELECT id,Region,Battlegroup,contentSetId FROM realmlist WHERE gamebuild=@build AND (id=@local OR contentSetId=@content) ORDER BY (contentSetId=@content) DESC,(id=@local) DESC", connection);
        command.Parameters.AddWithValue("@build", local.Build);
        command.Parameters.AddWithValue("@local", local.Id.Index);
        command.Parameters.AddWithValue("@content", request.ContentSet);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = new RealmId(reader.GetByte(1), reader.GetByte(2), reader.GetInt32(0));
            Realm realm = Global.RealmMgr.GetRealm(id);
            // The local world is online even while a realm-list refresh is pending.
            if (id.Equals(local.Id)) realm = local;
            if (realm == null || (!id.Equals(local.Id) && realm.Flags.HasAnyFlag(RealmFlags.Offline))) continue;
            return LastPlayed(realm, reader.GetUInt32(3), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        return new BnetCall { Status = (uint)BattlenetRpcErrorCode.UtilServerUnknownRealm, Command = request.Command };
    }

    internal static BnetCall LastPlayed(Realm realm, uint contentSet, long now)
    {
        RealmBuildInfo build = Global.RealmMgr.GetBuildInfo(realm.Build);
        uint district = contentSet switch { 136 => 1, 137 => 2, 138 => 3, 140 => 4, _ => 0 };
        string json = JsonSerializer.Serialize(new
        {
            wowRealmAddress = realm.Id.GetAddress(), cfgTimezonesID = 1,
            populationState = Math.Max((int)realm.PopulationLevel, 1), cfgCategoriesID = realm.Timezone,
            version = new { versionMajor = build?.MajorVersion ?? 1, versionMinor = build?.MinorVersion ?? 60,
                versionRevision = build?.BugfixVersion ?? 1, versionBuild = realm.Build },
            cfgRealmsID = realm.Id.Index, gameServiceRegionId = 1,
            flags = (int)(realm.Flags & ~RealmFlags.Offline), name = realm.Name,
            cfgConfigsID = realm.GetConfigId(), cfgLanguagesID = 1, cfgContentSetID = contentSet,
            superDistrictID = district, useBleepChance = 0
        });
        var response = new ProtoWriter();
        void Attribute(string name, ProtoWriter value)
        {
            var attribute = new ProtoWriter(); attribute.String(1, name); attribute.Message(2, value);
            response.Message(1, attribute);
        }
        ProtoWriter Blob(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text + "\0");
            var value = new ProtoWriter(); value.Bytes(5, BitConverter.GetBytes(bytes.Length).Combine(ZLib.Compress(bytes)));
            return value;
        }
        Attribute("Param_RealmEntry", Blob("JamJSONRealmEntry:" + json));
        var time = new ProtoWriter(); time.Varint(2, unchecked((ulong)now)); Attribute("Param_LastPlayedTime", time);
        Attribute("Param_UtilityInfo", Blob("JSONUtilityInfo:{\"realmPermissions\":512}"));
        return new BnetCall { Body = response.ToArray(), Command = "Command_LastCharPlayedRequest" };
    }
}
