using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI;

public static class SIServiceSelection
{
    public static IEnumerable<SISpawn> Nearest(Vector3 origin, IEnumerable<SISpawn> spawns, Func<SISpawn, bool> eligible) =>
        spawns.Where(eligible).OrderBy(s => Vector3.DistanceSquared(origin, s.Position)).ThenBy(s => s.SpawnId);
    public static bool OwnsTrip(SITaskKind kind) => kind is SITaskKind.Train or SITaskKind.ProfessionTrain or SITaskKind.Vendor;
}

public sealed partial class SIPlayer
{
    bool OwnedServiceTrip => _task != null && SIServiceSelection.OwnsTrip(_task.Kind);

    // Scan the full database-backed spawn index each time a service is selected,
    // including services outside the loaded grids. A failed nearest NPC is skipped
    // before sorting, so it cannot hide the next closest usable service.
    IEnumerable<SISpawn> ServiceSpawns(IEnumerable<int> entries, SITaskKind kind, SIVendorPurpose purpose = SIVendorPurpose.Sell) =>
        SIServiceSelection.Nearest(Here, Knowledge.Spawns(entries.Distinct(), Array.Empty<int>(), me.GetMapId()), spawn =>
            SocialServiceAllowed(spawn) && ServiceSpawnVisible(spawn) && SpawnAvailable(spawn) && FriendlyTemplate(spawn.Entry) && !IsLearnedRouteBlocked(P(spawn.Position)) &&
            !Blacklisted(new SITask { Kind = kind, VendorPurpose = purpose, Destination = spawn }.Key));

    bool ServiceSpawnVisible(SISpawn spawn)
    {
        var data = Global.ObjectMgr.GetCreatureData(spawn.SpawnId);
        if (data != null && (!PhasingHandler.InDbPhaseShift(me, data.PhaseUseFlags, data.PhaseId, data.PhaseGroup) ||
            data.terrainSwapMap >= 0 && !me.GetPhaseShift().HasVisibleMapId(data.terrainSwapMap))) return false;
        var live = me.GetMap().GetCreatureBySpawnId(spawn.SpawnId);
        return live == null || live.IsAlive() && live.IsInWorld && me.InSamePhase(live);
    }

    SISpawn? NearestServiceSpawn(IEnumerable<int> entries, SITaskKind kind, SIVendorPurpose purpose = SIVendorPurpose.Sell) =>
        ServiceSpawns(entries, kind, purpose).Select(s => (SISpawn?)s).FirstOrDefault();
}
