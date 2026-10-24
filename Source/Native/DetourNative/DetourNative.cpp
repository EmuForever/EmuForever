// C API over upstream recastnavigation Detour (1.6.0) for CypherCore's managed wrapper
// (Source/Framework/RecastDetour/Detour/DetourNative.cs).
//
// Built with DT_POLYREF64 (64-bit polygon references, matching the installed mmaps) and
// DT_VIRTUAL_QUERYFILTER (for the SI threat-cost filter). Tile data is copied into Detour-owned
// memory and freed by Detour when the tile is removed, so the managed side never pins it.

#include "DetourNavMesh.h"
#include "DetourNavMeshQuery.h"
#include "DetourCommon.h"
#include "DetourAlloc.h"

#include <cmath>
#include <cstring>
#include <new>
#include <vector>

#define DTN_API extern "C" __declspec(dllexport)

namespace
{
    // dtQueryFilter plus the SI bot threat cost: a route segment that enters a hostile aggro circle
    // costs extra, so it is used only when nothing else connects. Mirrors SITravelThreat.Blocks.
    class ThreatFilter : public dtQueryFilter
    {
    public:
        struct Threat { float x, y, z, radius; };

        std::vector<Threat> threats;
        float origin[3] = { 0, 0, 0 }; // world coordinates (x, y, z)
        float threatCost = 0;

        float getCost(const float* pa, const float* pb,
            const dtPolyRef prevRef, const dtMeshTile* prevTile, const dtPoly* prevPoly,
            const dtPolyRef curRef, const dtMeshTile* curTile, const dtPoly* curPoly,
            const dtPolyRef nextRef, const dtMeshTile* nextTile, const dtPoly* nextPoly) const override
        {
            float cost = dtQueryFilter::getCost(pa, pb, prevRef, prevTile, prevPoly, curRef, curTile, curPoly, nextRef, nextTile, nextPoly);
            if (threats.empty())
                return cost;

            // Detour space is (y, z, x) of world space.
            const float fromX = pa[2], fromY = pa[0], fromZ = pa[1];
            const float toX = pb[2], toY = pb[0], toZ = pb[1];
            for (const Threat& threat : threats)
                if (Blocks(threat, fromX, fromY, fromZ, toX, toY, toZ))
                    return cost + threatCost;
            return cost;
        }

    private:
        bool Blocks(const Threat& threat, float fromX, float fromY, float fromZ, float toX, float toY, float toZ) const
        {
            if (std::fabs(fromZ - threat.z) > 6 && std::fabs(toZ - threat.z) > 6)
                return false;

            const float ax = fromX - threat.x, ay = fromY - threat.y;
            const float bx = toX - threat.x, by = toY - threat.y;
            const float sx = origin[0] - threat.x, sy = origin[1] - threat.y;
            const float ex = bx - ax, ey = by - ay;
            const float r2 = threat.radius * threat.radius;
            const float aLen = ax * ax + ay * ay, bLen = bx * bx + by * by, eLen = ex * ex + ey * ey;
            const float aDotE = ax * ex + ay * ey;

            // Permit escaping an existing aggro circle, never deeper entry or re-entry.
            if (sx * sx + sy * sy < r2 && aDotE >= 0 && bLen >= aLen)
                return false;

            float t = eLen < .0001f ? 0 : -aDotE / eLen;
            t = t < 0 ? 0 : (t > 1 ? 1 : t);
            const float cx = ax + ex * t, cy = ay + ey * t;
            return cx * cx + cy * cy < r2;
        }
    };

    bool CopyHeader(const dtMeshTile* tile, dtMeshHeader* out)
    {
        if (!tile || !tile->header)
            return false;
        if (out)
            *out = *tile->header;
        return true;
    }
}

// ---- dtNavMesh ---------------------------------------------------------------------------------

DTN_API dtStatus dtn_mesh_create(const dtNavMeshParams* params, dtNavMesh** result)
{
    *result = nullptr;
    dtNavMesh* mesh = dtAllocNavMesh();
    if (!mesh)
        return DT_FAILURE | DT_OUT_OF_MEMORY;
    dtStatus status = mesh->init(params);
    if (dtStatusFailed(status))
    {
        dtFreeNavMesh(mesh);
        return status;
    }
    *result = mesh;
    return status;
}

DTN_API void dtn_mesh_free(dtNavMesh* mesh) { dtFreeNavMesh(mesh); }

DTN_API void dtn_mesh_get_params(const dtNavMesh* mesh, dtNavMeshParams* result) { *result = *mesh->getParams(); }

DTN_API dtStatus dtn_mesh_add_tile(dtNavMesh* mesh, const unsigned char* data, int size, dtTileRef lastRef, dtTileRef* result)
{
    *result = 0;
    unsigned char* copy = static_cast<unsigned char*>(dtAlloc(size, DT_ALLOC_PERM));
    if (!copy)
        return DT_FAILURE | DT_OUT_OF_MEMORY;
    std::memcpy(copy, data, size);
    dtStatus status = mesh->addTile(copy, size, DT_TILE_FREE_DATA, lastRef, result);
    if (dtStatusFailed(status))
        dtFree(copy);
    return status;
}

DTN_API dtStatus dtn_mesh_remove_tile(dtNavMesh* mesh, dtTileRef ref) { return mesh->removeTile(ref, nullptr, nullptr); }

DTN_API int dtn_mesh_get_max_tiles(const dtNavMesh* mesh) { return mesh->getMaxTiles(); }

DTN_API int dtn_mesh_get_tile(const dtNavMesh* mesh, int index, dtMeshHeader* header)
{
    return CopyHeader(mesh->getTile(index), header) ? 1 : 0;
}

DTN_API int dtn_mesh_get_tile_at(const dtNavMesh* mesh, int x, int y, int layer, dtMeshHeader* header, dtTileRef* ref)
{
    const dtMeshTile* tile = mesh->getTileAt(x, y, layer);
    if (!CopyHeader(tile, header))
        return 0;
    if (ref)
        *ref = mesh->getTileRef(tile);
    return 1;
}

DTN_API int dtn_mesh_get_tile_by_ref(const dtNavMesh* mesh, dtTileRef ref, dtMeshHeader* header)
{
    return CopyHeader(mesh->getTileByRef(ref), header) ? 1 : 0;
}

DTN_API dtStatus dtn_mesh_get_tile_header_by_poly(const dtNavMesh* mesh, dtPolyRef ref, dtMeshHeader* header)
{
    const dtMeshTile* tile = nullptr;
    const dtPoly* poly = nullptr;
    dtStatus status = mesh->getTileAndPolyByRef(ref, &tile, &poly);
    if (dtStatusSucceed(status))
        CopyHeader(tile, header);
    return status;
}

DTN_API void dtn_mesh_calc_tile_loc(const dtNavMesh* mesh, const float* pos, int* tx, int* ty) { mesh->calcTileLoc(pos, tx, ty); }

DTN_API dtStatus dtn_mesh_get_poly_area(const dtNavMesh* mesh, dtPolyRef ref, unsigned char* area) { return mesh->getPolyArea(ref, area); }

DTN_API dtStatus dtn_mesh_get_poly_flags(const dtNavMesh* mesh, dtPolyRef ref, unsigned short* flags) { return mesh->getPolyFlags(ref, flags); }

DTN_API int dtn_mesh_is_valid_poly_ref(const dtNavMesh* mesh, dtPolyRef ref) { return mesh->isValidPolyRef(ref) ? 1 : 0; }

DTN_API dtStatus dtn_mesh_get_offmesh_endpoints(const dtNavMesh* mesh, dtPolyRef prevRef, dtPolyRef polyRef, float* startPos, float* endPos)
{
    return mesh->getOffMeshConnectionPolyEndPoints(prevRef, polyRef, startPos, endPos);
}

// ---- dtQueryFilter -----------------------------------------------------------------------------

DTN_API dtQueryFilter* dtn_filter_create() { return new (std::nothrow) ThreatFilter(); }
DTN_API void dtn_filter_free(dtQueryFilter* filter) { delete static_cast<ThreatFilter*>(filter); }
DTN_API unsigned short dtn_filter_get_include(const dtQueryFilter* filter) { return filter->getIncludeFlags(); }
DTN_API void dtn_filter_set_include(dtQueryFilter* filter, unsigned short flags) { filter->setIncludeFlags(flags); }
DTN_API unsigned short dtn_filter_get_exclude(const dtQueryFilter* filter) { return filter->getExcludeFlags(); }
DTN_API void dtn_filter_set_exclude(dtQueryFilter* filter, unsigned short flags) { filter->setExcludeFlags(flags); }
DTN_API float dtn_filter_get_area_cost(const dtQueryFilter* filter, int area) { return filter->getAreaCost(area); }
DTN_API void dtn_filter_set_area_cost(dtQueryFilter* filter, int area, float cost) { filter->setAreaCost(area, cost); }

// threats: count * (x, y, z, radius) in world coordinates; origin: world (x, y, z).
DTN_API void dtn_filter_set_threats(dtQueryFilter* filter, const float* threats, int count, const float* origin, float cost)
{
    ThreatFilter* threatFilter = static_cast<ThreatFilter*>(filter);
    threatFilter->threats.resize(count > 0 ? count : 0);
    if (count > 0)
        std::memcpy(threatFilter->threats.data(), threats, sizeof(ThreatFilter::Threat) * count);
    if (origin)
        dtVcopy(threatFilter->origin, origin);
    threatFilter->threatCost = cost;
}

// ---- dtNavMeshQuery ----------------------------------------------------------------------------

DTN_API dtStatus dtn_query_create(const dtNavMesh* mesh, int maxNodes, dtNavMeshQuery** result)
{
    *result = nullptr;
    dtNavMeshQuery* query = dtAllocNavMeshQuery();
    if (!query)
        return DT_FAILURE | DT_OUT_OF_MEMORY;
    dtStatus status = query->init(mesh, maxNodes);
    if (dtStatusFailed(status))
    {
        dtFreeNavMeshQuery(query);
        return status;
    }
    *result = query;
    return status;
}

DTN_API void dtn_query_free(dtNavMeshQuery* query) { dtFreeNavMeshQuery(query); }

DTN_API dtStatus dtn_query_find_nearest_poly(const dtNavMeshQuery* query, const float* center, const float* halfExtents,
    const dtQueryFilter* filter, dtPolyRef* nearestRef, float* nearestPt)
{
    return query->findNearestPoly(center, halfExtents, filter, nearestRef, nearestPt);
}

DTN_API dtStatus dtn_query_find_path(const dtNavMeshQuery* query, dtPolyRef startRef, dtPolyRef endRef, const float* startPos, const float* endPos,
    const dtQueryFilter* filter, dtPolyRef* path, int* pathCount, int maxPath)
{
    return query->findPath(startRef, endRef, startPos, endPos, filter, path, pathCount, maxPath);
}

DTN_API dtStatus dtn_query_find_straight_path(const dtNavMeshQuery* query, const float* startPos, const float* endPos,
    const dtPolyRef* path, int pathSize, float* straightPath, unsigned char* straightPathFlags, dtPolyRef* straightPathRefs,
    int* straightPathCount, int maxStraightPath, int options)
{
    return query->findStraightPath(startPos, endPos, path, pathSize, straightPath, straightPathFlags, straightPathRefs,
        straightPathCount, maxStraightPath, options);
}

DTN_API dtStatus dtn_query_move_along_surface(const dtNavMeshQuery* query, dtPolyRef startRef, const float* startPos, const float* endPos,
    const dtQueryFilter* filter, float* resultPos, dtPolyRef* visited, int* visitedCount, int maxVisitedSize)
{
    return query->moveAlongSurface(startRef, startPos, endPos, filter, resultPos, visited, visitedCount, maxVisitedSize);
}

DTN_API dtStatus dtn_query_raycast(const dtNavMeshQuery* query, dtPolyRef startRef, const float* startPos, const float* endPos,
    const dtQueryFilter* filter, float* t, float* hitNormal, dtPolyRef* path, int* pathCount, int maxPath)
{
    return query->raycast(startRef, startPos, endPos, filter, t, hitNormal, path, pathCount, maxPath);
}

DTN_API dtStatus dtn_query_get_poly_height(const dtNavMeshQuery* query, dtPolyRef ref, const float* pos, float* height)
{
    return query->getPolyHeight(ref, pos, height);
}

DTN_API dtStatus dtn_query_closest_point_on_poly(const dtNavMeshQuery* query, dtPolyRef ref, const float* pos, float* closest, int* posOverPoly)
{
    bool over = false;
    dtStatus status = query->closestPointOnPoly(ref, pos, closest, &over);
    if (posOverPoly)
        *posOverPoly = over ? 1 : 0;
    return status;
}

DTN_API dtStatus dtn_query_closest_point_on_poly_boundary(const dtNavMeshQuery* query, dtPolyRef ref, const float* pos, float* closest)
{
    return query->closestPointOnPolyBoundary(ref, pos, closest);
}

DTN_API dtStatus dtn_query_init_sliced_find_path(dtNavMeshQuery* query, dtPolyRef startRef, dtPolyRef endRef, const float* startPos,
    const float* endPos, const dtQueryFilter* filter, unsigned int options)
{
    return query->initSlicedFindPath(startRef, endRef, startPos, endPos, filter, options);
}

DTN_API dtStatus dtn_query_update_sliced_find_path(dtNavMeshQuery* query, int maxIter, int* doneIters)
{
    return query->updateSlicedFindPath(maxIter, doneIters);
}

DTN_API dtStatus dtn_query_finalize_sliced_find_path(dtNavMeshQuery* query, dtPolyRef* path, int* pathCount, int maxPath)
{
    return query->finalizeSlicedFindPath(path, pathCount, maxPath);
}

// Layout checks for the managed side.
DTN_API int dtn_sizeof_mesh_header() { return (int)sizeof(dtMeshHeader); }
DTN_API int dtn_sizeof_params() { return (int)sizeof(dtNavMeshParams); }
DTN_API int dtn_sizeof_poly_ref() { return (int)sizeof(dtPolyRef); }
