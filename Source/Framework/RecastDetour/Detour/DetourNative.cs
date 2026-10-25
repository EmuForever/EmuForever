// Managed front end for the prebuilt x64 DetourNative.dll (recastnavigation Detour 1.6.0).
//
// This replaces the old line-by-line C# port of Detour. That port turned every tile into millions of
// small objects (a class per polygon, link, detail mesh and BV node, each with its own little arrays):
// a 0.5 MB tile file became ~2.8 MB of managed heap, and with hundreds of tiles loaded for bots spread
// over two continents the garbage collector had several gigabytes of navmesh to trace on every full
// collection. Native Detour keeps each tile as its file bytes, outside the GC heap.
//
// The public surface keeps the old port's names and signatures, so callers did not change. Thread
// rules are Detour's: a dtNavMeshQuery is used by one thread at a time, and tiles are not added to or
// removed from a mesh while a query on it runs.

using System;
using System.Runtime.InteropServices;
using dtPolyRef = System.UInt64;
using dtStatus = System.UInt32;
using dtTileRef = System.UInt64;

public static partial class Detour
{
    public const int DT_VERTS_PER_POLYGON = 6;
    public const int DT_NAVMESH_MAGIC = 'D' << 24 | 'N' << 16 | 'A' << 8 | 'V';
    public const int DT_NAVMESH_VERSION = 7;
    public const ushort DT_EXT_LINK = 0x8000;
    public const uint DT_NULL_LINK = 0xffffffff;
    public const uint DT_OFFMESH_CON_BIDIR = 1;
    public const int DT_MAX_AREAS = 64;

    public enum dtTileFlags
    {
        /// The navigation mesh owns the tile memory and is responsible for freeing it.
        DT_TILE_FREE_DATA = 0x01,
    };

    /// Vertex flags returned by dtNavMeshQuery::findStraightPath.
    public enum dtStraightPathFlags
    {
        DT_STRAIGHTPATH_START = 0x01,               //< The vertex is the start position in the path.
        DT_STRAIGHTPATH_END = 0x02,                 //< The vertex is the end position in the path.
        DT_STRAIGHTPATH_OFFMESH_CONNECTION = 0x04,  //< The vertex is the start of an off-mesh connection.
    };

    /// Options for dtNavMeshQuery::findStraightPath.
    public enum dtStraightPathOptions
    {
        DT_STRAIGHTPATH_AREA_CROSSINGS = 0x01,  //< Add a vertex at every polygon edge crossing where area changes.
        DT_STRAIGHTPATH_ALL_CROSSINGS = 0x02,   //< Add a vertex at every polygon edge crossing.
    };

    /// Flags representing the type of a navigation mesh polygon.
    public enum dtPolyTypes
    {
        DT_POLYTYPE_GROUND = 0,
        DT_POLYTYPE_OFFMESH_CONNECTION = 1,
    };

    public class dtNavMeshParams
    {
        public float[] orig = new float[3];     //< The world space origin of the navigation mesh's tile space. [(x, y, z)]
        public float tileWidth;                 //< The width of each tile. (Along the x-axis.)
        public float tileHeight;                //< The height of each tile. (Along the z-axis.)
        public int maxTiles;                    //< The maximum number of tiles the navigation mesh can contain.
        public int maxPolys;                    //< The maximum number of polygons each tile can contain.

        public dtNavMeshParams Clone()
        {
            var copy = (dtNavMeshParams)MemberwiseClone();
            copy.orig = (float[])orig.Clone();
            return copy;
        }

        internal Native.Params ToNative()
        {
            var native = new Native.Params { TileWidth = tileWidth, TileHeight = tileHeight, MaxTiles = maxTiles, MaxPolys = maxPolys };
            unsafe { for (int i = 0; i < 3; ++i) native.Orig[i] = orig[i]; }
            return native;
        }

        internal static dtNavMeshParams FromNative(in Native.Params native)
        {
            var result = new dtNavMeshParams { tileWidth = native.TileWidth, tileHeight = native.TileHeight, maxTiles = native.MaxTiles, maxPolys = native.MaxPolys };
            unsafe { fixed (float* orig = native.Orig) for (int i = 0; i < 3; ++i) result.orig[i] = orig[i]; }
            return result;
        }
    }

    /// A copy of a tile's header. Tile contents stay in native memory.
    public class dtMeshHeader
    {
        public int magic, version, x, y, layer;
        public uint userId;
        public int polyCount, vertCount, maxLinkCount, detailMeshCount, detailVertCount, detailTriCount;
        public int bvNodeCount, offMeshConCount, offMeshBase;
        public float walkableHeight, walkableRadius, walkableClimb;
        public float[] bmin = new float[3];
        public float[] bmax = new float[3];
        public float bvQuantFactor;

        internal static unsafe dtMeshHeader FromNative(in Native.Header h)
        {
            var header = new dtMeshHeader
            {
                magic = h.Magic, version = h.Version, x = h.X, y = h.Y, layer = h.Layer, userId = h.UserId,
                polyCount = h.PolyCount, vertCount = h.VertCount, maxLinkCount = h.MaxLinkCount,
                detailMeshCount = h.DetailMeshCount, detailVertCount = h.DetailVertCount, detailTriCount = h.DetailTriCount,
                bvNodeCount = h.BvNodeCount, offMeshConCount = h.OffMeshConCount, offMeshBase = h.OffMeshBase,
                walkableHeight = h.WalkableHeight, walkableRadius = h.WalkableRadius, walkableClimb = h.WalkableClimb,
                bvQuantFactor = h.BvQuantFactor
            };
            fixed (float* bmin = h.Bmin, bmax = h.Bmax)
                for (int i = 0; i < 3; ++i) { header.bmin[i] = bmin[i]; header.bmax[i] = bmax[i]; }
            return header;
        }
    }

    /// A loaded tile as seen from managed code: its reference and a copy of its header.
    public class dtMeshTile
    {
        public dtTileRef tileRef;
        public dtMeshHeader header;
    }

    /// Placeholder kept for getTileAndPolyByRef's signature; polygon data stays native.
    public class dtPoly
    {
    }

    /// A tile's binary data as read from an .mmtile file (after the mmap tile header).
    public class dtRawTileData
    {
        public byte[] data;

        public int FromBytes(byte[] array, int start)
        {
            data = start == 0 ? array : array.AsSpan(start).ToArray();
            return array.Length;
        }
    }

    public class dtNavMesh
    {
        internal readonly Native.MeshHandle Handle = new();

        public dtStatus init(dtNavMeshParams navMeshParams)
        {
            if (!Handle.IsInvalid)
                return DT_FAILURE;
            var native = navMeshParams.ToNative();
            dtStatus status = Native.dtn_mesh_create(in native, out IntPtr mesh);
            if (dtStatusSucceed(status))
                Handle.Set(mesh);
            return status;
        }

        public dtNavMeshParams getParams()
        {
            Native.dtn_mesh_get_params(Handle, out Native.Params native);
            return dtNavMeshParams.FromNative(native);
        }

        /// The tile data is copied into Detour-owned memory; flags are ignored (the mesh always frees it).
        public dtStatus addTile(dtRawTileData rawTileData, int flags, dtTileRef lastRef, ref dtTileRef result)
        {
            byte[] data = rawTileData?.data;
            if (data == null || data.Length == 0)
                return DT_FAILURE | DT_INVALID_PARAM;
            return Native.dtn_mesh_add_tile(Handle, data, data.Length, lastRef, out result);
        }

        public dtStatus removeTile(dtTileRef tileRef, out dtRawTileData rawTileData)
        {
            rawTileData = null;
            return Native.dtn_mesh_remove_tile(Handle, tileRef);
        }

        public dtMeshTile getTileAt(int x, int y, int layer)
        {
            if (Native.dtn_mesh_get_tile_at(Handle, x, y, layer, out Native.Header header, out dtTileRef tileRef) == 0)
                return null;
            return new dtMeshTile { tileRef = tileRef, header = dtMeshHeader.FromNative(header) };
        }

        public dtMeshTile getTileByRef(dtTileRef tileRef)
        {
            if (Native.dtn_mesh_get_tile_by_ref(Handle, tileRef, out Native.Header header) == 0)
                return null;
            return new dtMeshTile { tileRef = tileRef, header = dtMeshHeader.FromNative(header) };
        }

        public int getMaxTiles() => Native.dtn_mesh_get_max_tiles(Handle);

        /// Tile slot i; its header is null when the slot is empty.
        public dtMeshTile getTile(int i)
        {
            var tile = new dtMeshTile();
            if (Native.dtn_mesh_get_tile(Handle, i, out Native.Header header) != 0)
                tile.header = dtMeshHeader.FromNative(header);
            return tile;
        }

        public void calcTileLoc(float[] pos, ref int tx, ref int ty) => Native.dtn_mesh_calc_tile_loc(Handle, pos, out tx, out ty);

        public dtStatus getTileAndPolyByRef(dtPolyRef polyRef, ref dtMeshTile tile, ref dtPoly poly)
        {
            dtStatus status = Native.dtn_mesh_get_tile_header_by_poly(Handle, polyRef, out Native.Header header);
            if (dtStatusSucceed(status))
            {
                tile ??= new dtMeshTile();
                tile.header = dtMeshHeader.FromNative(header);
                poly ??= new dtPoly();
            }
            return status;
        }

        public dtStatus getPolyArea(dtPolyRef polyRef, ref byte resultArea) => Native.dtn_mesh_get_poly_area(Handle, polyRef, out resultArea);

        public dtStatus getPolyFlags(dtPolyRef polyRef, ref ushort resultFlags) => Native.dtn_mesh_get_poly_flags(Handle, polyRef, out resultFlags);

        public bool isValidPolyRef(dtPolyRef polyRef) => Native.dtn_mesh_is_valid_poly_ref(Handle, polyRef) != 0;

        public dtStatus getOffMeshConnectionPolyEndPoints(dtPolyRef prevRef, dtPolyRef polyRef, float[] startPos, float[] endPos) =>
            Native.dtn_mesh_get_offmesh_endpoints(Handle, prevRef, polyRef, startPos, endPos);
    }

    /// Include/exclude flags and area costs, plus optional route threats (see setThreats).
    public class dtQueryFilter
    {
        internal readonly Native.FilterHandle Handle = Native.FilterHandle.Create();

        public float getAreaCost(int i) => Native.dtn_filter_get_area_cost(Handle, i);
        public void setAreaCost(int i, float cost) => Native.dtn_filter_set_area_cost(Handle, i, cost);
        public ushort getIncludeFlags() => Native.dtn_filter_get_include(Handle);
        public void setIncludeFlags(ushort flags) => Native.dtn_filter_set_include(Handle, flags);
        public ushort getExcludeFlags() => Native.dtn_filter_get_exclude(Handle);
        public void setExcludeFlags(ushort flags) => Native.dtn_filter_set_exclude(Handle, flags);

        /// Segments entering any of these circles cost <paramref name="cost"/> extra. threats holds
        /// (x, y, z, radius) per threat and origin is (x, y, z), both in world coordinates; a route
        /// starting inside a circle may leave it without the extra cost. An empty array clears them.
        public void setThreats(float[] threats, int count, float[] origin, float cost) =>
            Native.dtn_filter_set_threats(Handle, threats, count, origin, cost);
    }

    public class dtNavMeshQuery
    {
        internal readonly Native.QueryHandle Handle = new();
        dtNavMesh _mesh;

        public dtStatus init(dtNavMesh nav, int maxNodes)
        {
            if (!Handle.IsInvalid)
                return DT_FAILURE;
            dtStatus status = Native.dtn_query_create(nav.Handle, maxNodes, out IntPtr query);
            if (dtStatusSucceed(status))
            {
                Handle.Set(query);
                _mesh = nav;
            }
            return status;
        }

        public dtNavMesh getAttachedNavMesh() => _mesh;

        public dtStatus findNearestPoly(float[] center, float[] halfExtents, dtQueryFilter filter, ref dtPolyRef nearestRef, ref float[] nearestPt) =>
            Native.dtn_query_find_nearest_poly(Handle, center, halfExtents, filter.Handle, out nearestRef, nearestPt);

        public dtStatus findPath(dtPolyRef startRef, dtPolyRef endRef, float[] startPos, float[] endPos, dtQueryFilter filter, dtPolyRef[] path, ref uint pathCount, int maxPath)
        {
            dtStatus status = Native.dtn_query_find_path(Handle, startRef, endRef, startPos, endPos, filter.Handle, path, out int count, maxPath);
            pathCount = (uint)count;
            return status;
        }

        public dtStatus findStraightPath(float[] startPos, float[] endPos, dtPolyRef[] path, int pathSize, float[] straightPath, byte[] straightPathFlags,
            dtPolyRef[] straightPathRefs, ref int straightPathCount, int maxStraightPath, int options) =>
            Native.dtn_query_find_straight_path(Handle, startPos, endPos, path, pathSize, straightPath, straightPathFlags, straightPathRefs,
                out straightPathCount, maxStraightPath, options);

        public dtStatus moveAlongSurface(dtPolyRef startRef, float[] startPos, float[] endPos, dtQueryFilter filter, float[] resultPos, dtPolyRef[] visited,
            ref int visitedCount, int maxVisitedSize) =>
            Native.dtn_query_move_along_surface(Handle, startRef, startPos, endPos, filter.Handle, resultPos, visited, out visitedCount, maxVisitedSize);

        public dtStatus raycast(dtPolyRef startRef, float[] startPos, float[] endPos, dtQueryFilter filter, ref float t, float[] hitNormal,
            dtPolyRef[] path, ref uint pathCount, int maxPath)
        {
            dtStatus status = Native.dtn_query_raycast(Handle, startRef, startPos, endPos, filter.Handle, out t, hitNormal, path, out int count, maxPath);
            pathCount = (uint)count;
            return status;
        }

        public dtStatus getPolyHeight(dtPolyRef polyRef, float[] pos, ref float height) =>
            Native.dtn_query_get_poly_height(Handle, polyRef, pos, out height);

        public dtStatus closestPointOnPoly(dtPolyRef polyRef, float[] pos, float[] closest, ref bool posOverPoly)
        {
            dtStatus status = Native.dtn_query_closest_point_on_poly(Handle, polyRef, pos, closest, out int over);
            posOverPoly = over != 0;
            return status;
        }

        public dtStatus closestPointOnPolyBoundary(dtPolyRef polyRef, float[] pos, float[] closest) =>
            Native.dtn_query_closest_point_on_poly_boundary(Handle, polyRef, pos, closest);

        public dtStatus initSlicedFindPath(dtPolyRef startRef, dtPolyRef endRef, float[] startPos, float[] endPos, dtQueryFilter filter, uint options) =>
            Native.dtn_query_init_sliced_find_path(Handle, startRef, endRef, startPos, endPos, filter.Handle, options);

        public dtStatus updateSlicedFindPath(int maxIter, ref int doneIters) =>
            Native.dtn_query_update_sliced_find_path(Handle, maxIter, out doneIters);

        public dtStatus finalizeSlicedFindPath(dtPolyRef[] path, ref int pathCount, int maxPath) =>
            Native.dtn_query_finalize_sliced_find_path(Handle, path, out pathCount, maxPath);
    }

    internal static class Native
    {
        const string Library = "DetourNative";

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct Params
        {
            public fixed float Orig[3];
            public float TileWidth, TileHeight;
            public int MaxTiles, MaxPolys;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct Header
        {
            public int Magic, Version, X, Y, Layer;
            public uint UserId;
            public int PolyCount, VertCount, MaxLinkCount, DetailMeshCount, DetailVertCount, DetailTriCount;
            public int BvNodeCount, OffMeshConCount, OffMeshBase;
            public float WalkableHeight, WalkableRadius, WalkableClimb;
            public fixed float Bmin[3];
            public fixed float Bmax[3];
            public float BvQuantFactor;
        }

        internal abstract class NativeHandle : SafeHandle
        {
            protected NativeHandle() : base(IntPtr.Zero, true) { }
            public override bool IsInvalid => handle == IntPtr.Zero;
            internal void Set(IntPtr value) => SetHandle(value);
        }

        internal sealed class MeshHandle : NativeHandle
        {
            protected override bool ReleaseHandle() { dtn_mesh_free(handle); return true; }
        }

        internal sealed class QueryHandle : NativeHandle
        {
            protected override bool ReleaseHandle() { dtn_query_free(handle); return true; }
        }

        internal sealed class FilterHandle : NativeHandle
        {
            internal static FilterHandle Create()
            {
                var filter = new FilterHandle();
                IntPtr native = dtn_filter_create();
                if (native == IntPtr.Zero) throw new OutOfMemoryException("Cannot create Detour query filter");
                filter.Set(native);
                return filter;
            }
            protected override bool ReleaseHandle() { dtn_filter_free(handle); return true; }
        }

        static Native()
        {
            // The managed structs must match the native build (DT_POLYREF64).
            if (dtn_sizeof_poly_ref() != sizeof(ulong) || dtn_sizeof_mesh_header() != Marshal.SizeOf<Header>() || dtn_sizeof_params() != Marshal.SizeOf<Params>())
                throw new InvalidOperationException("DetourNative.dll does not match the managed Detour wrapper (expected a DT_POLYREF64 build).");
        }

        [DllImport(Library)] internal static extern int dtn_sizeof_mesh_header();
        [DllImport(Library)] internal static extern int dtn_sizeof_params();
        [DllImport(Library)] internal static extern int dtn_sizeof_poly_ref();

        [DllImport(Library)] internal static extern dtStatus dtn_mesh_create(in Params navMeshParams, out IntPtr mesh);
        [DllImport(Library)] internal static extern void dtn_mesh_free(IntPtr mesh);
        [DllImport(Library)] internal static extern void dtn_mesh_get_params(MeshHandle mesh, out Params result);
        [DllImport(Library)] internal static extern dtStatus dtn_mesh_add_tile(MeshHandle mesh, byte[] data, int size, dtTileRef lastRef, out dtTileRef result);
        [DllImport(Library)] internal static extern dtStatus dtn_mesh_remove_tile(MeshHandle mesh, dtTileRef tileRef);
        [DllImport(Library)] internal static extern int dtn_mesh_get_max_tiles(MeshHandle mesh);
        [DllImport(Library)] internal static extern int dtn_mesh_get_tile(MeshHandle mesh, int index, out Header header);
        [DllImport(Library)] internal static extern int dtn_mesh_get_tile_at(MeshHandle mesh, int x, int y, int layer, out Header header, out dtTileRef tileRef);
        [DllImport(Library)] internal static extern int dtn_mesh_get_tile_by_ref(MeshHandle mesh, dtTileRef tileRef, out Header header);
        [DllImport(Library)] internal static extern dtStatus dtn_mesh_get_tile_header_by_poly(MeshHandle mesh, dtPolyRef polyRef, out Header header);
        [DllImport(Library)] internal static extern void dtn_mesh_calc_tile_loc(MeshHandle mesh, float[] pos, out int tx, out int ty);
        [DllImport(Library)] internal static extern dtStatus dtn_mesh_get_poly_area(MeshHandle mesh, dtPolyRef polyRef, out byte area);
        [DllImport(Library)] internal static extern dtStatus dtn_mesh_get_poly_flags(MeshHandle mesh, dtPolyRef polyRef, out ushort flags);
        [DllImport(Library)] internal static extern int dtn_mesh_is_valid_poly_ref(MeshHandle mesh, dtPolyRef polyRef);
        [DllImport(Library)] internal static extern dtStatus dtn_mesh_get_offmesh_endpoints(MeshHandle mesh, dtPolyRef prevRef, dtPolyRef polyRef, float[] startPos, float[] endPos);

        [DllImport(Library)] internal static extern IntPtr dtn_filter_create();
        [DllImport(Library)] internal static extern void dtn_filter_free(IntPtr filter);
        [DllImport(Library)] internal static extern ushort dtn_filter_get_include(FilterHandle filter);
        [DllImport(Library)] internal static extern void dtn_filter_set_include(FilterHandle filter, ushort flags);
        [DllImport(Library)] internal static extern ushort dtn_filter_get_exclude(FilterHandle filter);
        [DllImport(Library)] internal static extern void dtn_filter_set_exclude(FilterHandle filter, ushort flags);
        [DllImport(Library)] internal static extern float dtn_filter_get_area_cost(FilterHandle filter, int area);
        [DllImport(Library)] internal static extern void dtn_filter_set_area_cost(FilterHandle filter, int area, float cost);
        [DllImport(Library)] internal static extern void dtn_filter_set_threats(FilterHandle filter, float[] threats, int count, float[] origin, float cost);

        [DllImport(Library)] internal static extern dtStatus dtn_query_create(MeshHandle mesh, int maxNodes, out IntPtr query);
        [DllImport(Library)] internal static extern void dtn_query_free(IntPtr query);
        [DllImport(Library)] internal static extern dtStatus dtn_query_find_nearest_poly(QueryHandle query, float[] center, float[] halfExtents, FilterHandle filter, out dtPolyRef nearestRef, float[] nearestPt);
        [DllImport(Library)] internal static extern dtStatus dtn_query_find_path(QueryHandle query, dtPolyRef startRef, dtPolyRef endRef, float[] startPos, float[] endPos, FilterHandle filter, dtPolyRef[] path, out int pathCount, int maxPath);
        [DllImport(Library)] internal static extern dtStatus dtn_query_find_straight_path(QueryHandle query, float[] startPos, float[] endPos, dtPolyRef[] path, int pathSize, float[] straightPath, byte[] straightPathFlags, dtPolyRef[] straightPathRefs, out int straightPathCount, int maxStraightPath, int options);
        [DllImport(Library)] internal static extern dtStatus dtn_query_move_along_surface(QueryHandle query, dtPolyRef startRef, float[] startPos, float[] endPos, FilterHandle filter, float[] resultPos, dtPolyRef[] visited, out int visitedCount, int maxVisitedSize);
        [DllImport(Library)] internal static extern dtStatus dtn_query_raycast(QueryHandle query, dtPolyRef startRef, float[] startPos, float[] endPos, FilterHandle filter, out float t, float[] hitNormal, dtPolyRef[] path, out int pathCount, int maxPath);
        [DllImport(Library)] internal static extern dtStatus dtn_query_get_poly_height(QueryHandle query, dtPolyRef polyRef, float[] pos, out float height);
        [DllImport(Library)] internal static extern dtStatus dtn_query_closest_point_on_poly(QueryHandle query, dtPolyRef polyRef, float[] pos, float[] closest, out int posOverPoly);
        [DllImport(Library)] internal static extern dtStatus dtn_query_closest_point_on_poly_boundary(QueryHandle query, dtPolyRef polyRef, float[] pos, float[] closest);
        [DllImport(Library)] internal static extern dtStatus dtn_query_init_sliced_find_path(QueryHandle query, dtPolyRef startRef, dtPolyRef endRef, float[] startPos, float[] endPos, FilterHandle filter, uint options);
        [DllImport(Library)] internal static extern dtStatus dtn_query_update_sliced_find_path(QueryHandle query, int maxIter, out int doneIters);
        [DllImport(Library)] internal static extern dtStatus dtn_query_finalize_sliced_find_path(QueryHandle query, dtPolyRef[] path, out int pathCount, int maxPath);
    }
}
