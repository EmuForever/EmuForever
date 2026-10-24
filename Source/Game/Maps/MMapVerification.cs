using Game.Movement;
using System;

namespace Game
{
    // Offline check that generated mmaps load through MMapManager and answer a real path query.
    public static class MMapVerification
    {
        // WoW (x, y, z) to Detour (y, z, x).
        static float[] Detour3(float x, float y, float z) => new[] { y, z, x };

        public static string Verify(string dataDir, int mapId, int tileX, int tileY, float[] from, float[] to)
        {
            if (!Global.MMapMgr.LoadMap(dataDir, mapId, tileX, tileY))
                throw new InvalidOperationException($"Tile {mapId:D4}_{tileX:D2}_{tileY:D2} did not load.");
            Detour.dtNavMesh mesh = Global.MMapMgr.GetNavMesh(mapId);
            var query = new Detour.dtNavMeshQuery();
            if (Detour.dtStatusFailed(query.init(mesh, 1024)))
                throw new InvalidOperationException("Navmesh query could not be initialised.");
            var filter = new Detour.dtQueryFilter();
            filter.setIncludeFlags((ushort)(NavTerrainFlag.Ground | NavTerrainFlag.GroundSteep | NavTerrainFlag.Water));
            float[] extents = { 3, 50, 3 };
            ulong Nearest(float[] point, out float[] snapped)
            {
                ulong reference = 0;
                snapped = new float[3];
                query.findNearestPoly(point, extents, filter, ref reference, ref snapped);
                return reference;
            }
            float[] start = Detour3(from[0], from[1], from[2]), end = Detour3(to[0], to[1], to[2]);
            ulong startPoly = Nearest(start, out float[] startPoint), endPoly = Nearest(end, out float[] endPoint);
            if (startPoly == 0 || endPoly == 0)
                throw new InvalidOperationException("No navmesh polygon under the test points.");
            ulong[] polys = new ulong[256];
            uint count = 0;
            uint status = query.findPath(startPoly, endPoly, startPoint, endPoint, filter, polys, ref count, polys.Length);
            if (Detour.dtStatusFailed(status) || count == 0)
                throw new InvalidOperationException("Navmesh found no path between the test points.");
            bool partial = (status & Detour.DT_PARTIAL_RESULT) != 0;
            return $"loaded {mapId:D4}_{tileX:D2}_{tileY:D2}, path of {count} polygons{(partial ? " (partial)" : "")}";
        }
    }
}
