using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using System;
using System.Linq;

namespace Game.BattleGrounds;

public partial class BattlegroundManager
{
    int? _warsongMap;
    internal static int? ParseWarsongMap(string text) => text.Trim().ToLowerInvariant() switch
    {
        "all" => null,
        "3277" or "489" => 489,
        "2996" => 2996,
        _ => throw new ArgumentException("Use .bg wsg <3277|489|2996|all>. 3277 is the classic WSG zone; its map ID is 489.")
    };

    public int[] GetWarsongMaps() => _warsongMap is int map ? [map] : [489,2996];

    public void SetWarsongMaps(string selection)
    {
        int? selected=ParseWarsongMap(selection);
        foreach(int map in selected is int single?new[]{single}:new[]{489,2996})
            if(!WarsongMapReady(map))
                throw new ArgumentException($"WSG map {map} is missing its map record or flag spawns. Apply docs/sql/2026_11_02_wsg_winter_map.sql to the world database and restart before enabling it.");
        _warsongMap=selected;
    }

    bool WarsongMapReady(int map) => CliDB.MapStorage.ContainsKey(map)&&
        Global.ObjectMgr.GetAllGameObjectData().Values.Any(s=>s.MapId==map&&s.Id==179830)&&
        Global.ObjectMgr.GetAllGameObjectData().Values.Any(s=>s.MapId==map&&s.Id==179831);

    internal int SelectWarsongMap()
    {
        var maps=GetWarsongMaps().Where(WarsongMapReady).ToArray();
        if(maps.Length==0)return 0;
        return maps[Random.Shared.Next(maps.Length)];
    }

    // Matchmaking can still fill an existing match after a GM changes the map
    // selection. The selection controls new matches, not matches already running.
    internal int[] GetQueueMaps(BattlegroundTemplate template) => template.Id==BattlegroundTypeId.WS
        ? [489,2996] : template.BattlemasterEntry.MapId.Where(m=>m!=-1).Select(m=>(int)m).ToArray();
}

public partial class BattlegroundTemplate
{
    public int? InstanceMapId;
    public int MapId => InstanceMapId??BattlemasterEntry.MapId[0];

    internal BattlegroundTemplate ForMap(int map)
    {
        var copy=(BattlegroundTemplate)MemberwiseClone();
        copy.InstanceMapId=map;
        copy.StartLocation=StartLocation.Select(start=>RemapLocation(start,map)).ToArray();
        return copy;
    }

    internal static WorldSafeLocsEntry RemapLocation(WorldSafeLocsEntry start,int map)
    {
        if(start==null)return null;
        var location=new WorldLocation(start.Loc);location.SetMapId(map);
        return new WorldSafeLocsEntry { Id=start.Id,Loc=location };
    }
}
