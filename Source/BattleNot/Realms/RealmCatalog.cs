using System.Net;
using System.Net.Sockets;

namespace BattleNot;

sealed class RealmCatalog
{
    readonly AppConfig _config;
    readonly object _gate = new();
    List<RealmEntry> _realms = [];
    Dictionary<uint, BuildInfo> _builds = [];
    string _reachability = "";

    public RealmCatalog(AppConfig config) => _config = config;

    public async Task Refresh(RealmDb db)
    {
        List<RealmEntry> realms = await db.LoadRealms();
        Dictionary<uint, BuildInfo> builds = await db.LoadBuilds();
        await Probe(realms);
        string reachability = string.Join(", ", realms.Select(realm => realm.Name + (realm.WorldUp ? " ↑" : " ⬇")));
        lock (_gate)
        {
            _realms = realms;
            _builds = builds;
        }
        if (reachability != _reachability)
        {
            _reachability = reachability;
            Log.outInfo(LogFilter.Sql, "Realm ports: " + reachability);
        }
    }

    static async Task Probe(List<RealmEntry> realms)
    {
        await Task.WhenAll(realms.Select(async realm =>
        {
            foreach (IPAddress ip in realm.Addresses)
            {
                if (await PortOpen(ip, realm.Port))
                {
                    realm.WorldUp = true;
                    return;
                }
            }
            realm.WorldUp = false;
        }));
    }

    static async Task<bool> PortOpen(IPAddress ip, int port)
    {
        try
        {
            using var tcp = new TcpClient();
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await tcp.ConnectAsync(ip, port, cancel.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<RealmEntry> Snapshot()
    {
        lock (_gate)
            return _realms.ToArray();
    }

    // Online realms whose gamebuild is the selected client, and only when that exact version is in build_info.
    public IReadOnlyList<RealmEntry> OnlineForVersion(uint major, uint minor, uint revision, uint build)
    {
        lock (_gate)
        {
            if (!_builds.TryGetValue(build, out BuildInfo? info))
                return Array.Empty<RealmEntry>();
            if (info.Major != major || info.Minor != minor || info.Revision != revision)
                return Array.Empty<RealmEntry>();
            return _realms.Where(realm => realm.WorldUp && realm.Build == build).ToArray();
        }
    }

    public RealmEntry? Find(uint address)
    {
        lock (_gate)
            return _realms.FirstOrDefault(r => r.Address == address);
    }

    public RealmEntry? FindContentSet(uint contentSet)
    {
        lock (_gate)
            return _realms.FirstOrDefault(r => r.ContentSetId == contentSet && r.PopulationState != 0);
    }

    public RealmEntry? FirstOnline()
    {
        lock (_gate)
            return _realms.FirstOrDefault(r => r.PopulationState != 0) ?? _realms.FirstOrDefault();
    }

    public IReadOnlyList<BuildInfo> ClientVersions()
    {
        lock (_gate)
        {
            var list = new List<BuildInfo>();
            var seen = new HashSet<uint>();
            foreach (RealmEntry realm in _realms)
            {
                if (!seen.Add(realm.Build))
                    continue;
                if (_builds.TryGetValue(realm.Build, out BuildInfo? info))
                    list.Add(info);
            }
            return list;
        }
    }

    public bool TryVersionText(uint build, out string text)
    {
        lock (_gate)
        {
            if (_builds.TryGetValue(build, out BuildInfo? info))
            {
                text = info.Major + "." + info.Minor + "." + info.Revision + "." + info.Build;
                return true;
            }
        }
        text = "";
        return false;
    }

    // The build the realms are running, when that exact version is in build_info.
    public BuildInfo? TargetVersion()
    {
        lock (_gate)
        {
            uint chosen = 0;
            int count = 0;
            foreach (RealmEntry realm in _realms)
            {
                if (!_builds.ContainsKey(realm.Build))
                    continue;
                int n = 0;
                foreach (RealmEntry other in _realms)
                    if (other.Build == realm.Build)
                        n++;
                if (n > count)
                {
                    count = n;
                    chosen = realm.Build;
                }
            }
            return chosen != 0 && _builds.TryGetValue(chosen, out BuildInfo? info) ? info : null;
        }
    }

    public BuildInfo Version(uint build)
    {
        lock (_gate)
        {
            if (_builds.TryGetValue(build, out BuildInfo? info))
                return info;
        }
        if (build >= 70000)
            return new BuildInfo { Major = 1, Minor = 60, Revision = 1, Build = build };
        return new BuildInfo { Major = 6, Minor = 2, Revision = 4, Build = build };
    }

    public static uint SuperDistrictFor(uint contentSet) => contentSet switch
    {
        136 => 1,
        137 => 2,
        138 => 3,
        140 => 4,
        _ => 0
    };

    public static uint ContentSetFor(uint superDistrict) => superDistrict switch
    {
        1 => 136,
        2 => 137,
        3 => 138,
        4 => 140,
        _ => 0
    };

    public string SuperDistrictEntries()
    {
        var offered = new List<string>();
        var all = new List<string>();
        IReadOnlyList<RealmEntry> realms = Snapshot();
        for (uint id = 1; id <= 5; id++)
        {
            string entry = $"{{\"superDistrictID\":{id},\"disallowLogin\":false,\"holdDownUntilTime\":0}}";
            all.Add(entry);
            uint content = ContentSetFor(id);
            if (content != 0 && realms.Any(r => r.ContentSetId == content && r.PopulationState != 0))
                offered.Add(entry);
        }
        return string.Join(",", offered.Count > 0 ? offered : all);
    }

    public string RealmEntryJson(RealmEntry realm, uint clientBuild, byte security)
    {
        if (realm.PopulationState == 0 || realm.Build != clientBuild || security < realm.AllowedSecurity)
            return "";
        return Fill(realm, clientBuild, security);
    }

    public byte[] RealmListBlob(uint clientBuild, byte security, string subRegion)
    {
        var parts = new List<string>();
        foreach (RealmEntry realm in Snapshot())
        {
            if (realm.SubRegion != subRegion)
                continue;
            parts.Add("{\"wowRealmAddress\":" + realm.Address + ",\"update\":" + Fill(realm, clientBuild, security) + ",\"deleting\":false}");
        }
        return JsonText.Compress("JSONRealmListUpdates:{\"updates\":[" + string.Join(",", parts) + "]}");
    }

    string Fill(RealmEntry realm, uint clientBuild, byte security)
    {
        BuildInfo version = Version(realm.Build);
        byte flags = realm.Flags;
        if (realm.Build != clientBuild)
            flags |= 0x01;
        byte population = security >= realm.AllowedSecurity || realm.PopulationState == 0 ? realm.PopulationState : (byte)7;
        uint config = realm.Type < 14 ? (uint)realm.Type + 1 : 1;
        uint content = realm.ContentSetId != 0 ? realm.ContentSetId : _config.CfgContentSetID;
        string json =
            "{\"wowRealmAddress\":" + realm.Address +
            ",\"cfgTimezonesID\":1,\"populationState\":" + population +
            ",\"cfgCategoriesID\":" + realm.Timezone +
            ",\"version\":{\"versionMajor\":" + version.Major + ",\"versionMinor\":" + version.Minor +
            ",\"versionRevision\":" + version.Revision + ",\"versionBuild\":" + version.Build + "}" +
            ",\"cfgRealmsID\":" + realm.Id +
            // enUS client region is 1. realmlist.Region (70) is only the realm-address byte.
            // The client logs this field as 0 when it is missing, and CatalogShop_C.cpp(281) then rejects the shop.
            ",\"gameServiceRegionId\":1,\"flags\":" + flags +
            ",\"name\":" + JsonText.Quote(realm.Name) +
            ",\"cfgConfigsID\":" + config + ",\"cfgLanguagesID\":1,\"cfgContentSetID\":" + content +
            ",\"useBleepChance\":0}";
        return AddSuperDistrict(json);
    }

    string AddSuperDistrict(string json)
    {
        const string key = "\"cfgContentSetID\":";
        int pos = 0;
        while ((pos = json.IndexOf(key, pos, StringComparison.Ordinal)) >= 0)
        {
            int start = pos + key.Length;
            int end = start;
            while (end < json.Length && char.IsDigit(json[end]))
                end++;
            uint content = uint.TryParse(json.AsSpan(start, end - start), out uint parsed) ? parsed : 0;
            uint district = SuperDistrictFor(content);
            if (district == 0)
                district = _config.SuperDistrictID;
            string insert = ",\"superDistrictID\":" + district;
            json = json.Insert(end, insert);
            pos = end + insert.Length;
        }
        return json;
    }
}
