// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.Collision
{
    public enum VMAPLoadResult
    {
        Error,
        OK,
        Ignored
    }

    public enum LoadResult
    {
        Success,
        FileNotFound,
        VersionMismatch,
        ReadFromFileFailed,
        DisabledInConfig
    }

    public class VMapManager : Singleton<VMapManager>
    {
        VMapManager() { }

        public static string VMapPath = Global.WorldMgr.GetDataPath() + "/vmaps/";

        public void Initialize(MultiMap<int, int> mapData)
        {
            foreach (var pair in mapData)
                iParentMapData[pair.Value] = pair.Key;
        }

        public LoadResult LoadMap(int mapId, int x, int y)
        {
            if (!IsMapLoadingEnabled())
                return LoadResult.DisabledInConfig;

            var instanceTree = iInstanceMapTrees.LookupByKey(mapId);
            if (instanceTree == null)
            {
                string filename = VMapPath + GetMapFileName(mapId);
                StaticMapTree newTree = new(mapId);
                LoadResult treeInitResult = newTree.InitMap(filename);
                if (treeInitResult != LoadResult.Success)
                    return treeInitResult;

                iInstanceMapTrees.Add(mapId, newTree);

                instanceTree = newTree;
            }

            return instanceTree.LoadMapTile(x, y, this);
        }

        public void UnloadMap(int mapId, int x, int y)
        {
            var instanceTree = iInstanceMapTrees.LookupByKey(mapId);
            if (instanceTree != null)
            {
                instanceTree.UnloadMapTile(x, y, this);
                if (instanceTree.NumLoadedTiles() == 0)
                {
                    iInstanceMapTrees.Remove(mapId);
                }
            }
        }

        public void UnloadMap(int mapId)
        {
            var instanceTree = iInstanceMapTrees.LookupByKey(mapId);
            if (instanceTree != null)
            {
                instanceTree.UnloadMap(this);
                if (instanceTree.NumLoadedTiles() == 0)
                {
                    iInstanceMapTrees.Remove(mapId);
                }
            }
        }

        public bool IsInLineOfSight(int mapId, float x1, float y1, float z1, float x2, float y2, float z2, ModelIgnoreFlags ignoreFlags)
        {
            if (!IsLineOfSightCalcEnabled() || Global.DisableMgr.IsVMAPDisabledFor(mapId, DisableFlags.VmapLOS))
                return true;

            var instanceTree = iInstanceMapTrees.LookupByKey(mapId);
            if (instanceTree != null)
            {
                Vector3 pos1 = ConvertPositionToInternalRep(x1, y1, z1);
                Vector3 pos2 = ConvertPositionToInternalRep(x2, y2, z2);
                if (pos1 != pos2)
                    return instanceTree.IsInLineOfSight(pos1, pos2, ignoreFlags);
            }

            return true;
        }

        public IReadOnlyList<ModelInstance> GetEmbeddedSmallModels(int mapId, Vector3 point)
        {
            var tree = iInstanceMapTrees.LookupByKey(mapId);
            return tree == null ? Array.Empty<ModelInstance>() : tree.GetEmbeddedSmallModels(ConvertPositionToInternalRep(point.X,point.Y,point.Z));
        }
        public bool MovingOutOfSmallModels(Vector3 from,Vector3 to,IReadOnlyList<ModelInstance> models)
        {
            from = ConvertPositionToInternalRep(from.X,from.Y,from.Z); to = ConvertPositionToInternalRep(to.X,to.Y,to.Z);
            foreach (var model in models) if (!model.MovesOutward(from,to)) return false;
            return true;
        }
        public bool OutsideSmallModels(Vector3 point, IReadOnlyList<ModelInstance> models)
        {
            point = ConvertPositionToInternalRep(point.X,point.Y,point.Z);
            foreach (var model in models)
                if (point.X >= model.iBound.Lo.X - .35f && point.X <= model.iBound.Hi.X + .35f &&
                    point.Y >= model.iBound.Lo.Y - .35f && point.Y <= model.iBound.Hi.Y + .35f &&
                    point.Z >= model.iBound.Lo.Z - .35f && point.Z <= model.iBound.Hi.Z + .35f) return false;
            return true;
        }
        public bool IsInLineOfSightExcluding(int mapId, Vector3 from, Vector3 to, IReadOnlySet<uint> ignored)
        {
            var tree = iInstanceMapTrees.LookupByKey(mapId);
            return tree == null || tree.IsInLineOfSight(ConvertPositionToInternalRep(from.X,from.Y,from.Z),
                ConvertPositionToInternalRep(to.X,to.Y,to.Z), ModelIgnoreFlags.Nothing, ignored);
        }

        // World-space bounds of the nearest static model crossing the segment, or null when clear.
        public (uint Id, bool M2, Vector3 Lo, Vector3 Hi)? FindLineBlocker(int mapId, Vector3 from, Vector3 to, IReadOnlySet<uint> ignored = null)
        {
            if (!IsLineOfSightCalcEnabled() || Global.DisableMgr.IsVMAPDisabledFor(mapId, DisableFlags.VmapLOS))
                return null;
            var tree = iInstanceMapTrees.LookupByKey(mapId);
            var hit = tree?.GetFirstHit(ConvertPositionToInternalRep(from.X, from.Y, from.Z), ConvertPositionToInternalRep(to.X, to.Y, to.Z), ignored);
            if (hit == null)
                return null;
            // Internal X/Y are mirrored around the map center, so the corners swap.
            Vector3 a = ConvertPositionToInternalRep(hit.iBound.Hi.X, hit.iBound.Hi.Y, hit.iBound.Lo.Z);
            Vector3 b = ConvertPositionToInternalRep(hit.iBound.Lo.X, hit.iBound.Lo.Y, hit.iBound.Hi.Z);
            return (hit.Id, hit.GetWorldModel()?.Flags is uint flags && (flags & (uint)ModelFlags.M2) != 0, a, b);
        }

        public IReadOnlySet<uint> EmbeddedSmallModelIds(int mapId, Vector3 point)
        {
            var models = GetEmbeddedSmallModels(mapId, point);
            return models.Count == 0 ? null : models.Select(model => model.Id).ToHashSet();
        }

        public bool GetObjectHitPos(int mapId, float x1, float y1, float z1, float x2, float y2, float z2, out float rx, out float ry, out float rz, float modifyDist)
        {
            if (IsLineOfSightCalcEnabled() && !Global.DisableMgr.IsVMAPDisabledFor(mapId, DisableFlags.VmapLOS))
            {
                var instanceTree = iInstanceMapTrees.LookupByKey(mapId);
                if (instanceTree != null)
                {
                    Vector3 resultPos;
                    Vector3 pos1 = ConvertPositionToInternalRep(x1, y1, z1);
                    Vector3 pos2 = ConvertPositionToInternalRep(x2, y2, z2);
                    bool result = instanceTree.GetObjectHitPos(pos1, pos2, out resultPos, modifyDist);
                    resultPos = ConvertPositionToInternalRep(resultPos.X, resultPos.Y, resultPos.Z);
                    rx = resultPos.X;
                    ry = resultPos.Y;
                    rz = resultPos.Z;
                    return result;
                }
            }

            rx = x2;
            ry = y2;
            rz = z2;

            return false;
        }

        public float GetHeight(int mapId, float x, float y, float z, float maxSearchDist)
        {
            if (IsHeightCalcEnabled() && !Global.DisableMgr.IsVMAPDisabledFor(mapId, DisableFlags.VmapHeight))
            {
                var instanceTree = iInstanceMapTrees.LookupByKey(mapId);
                if (instanceTree != null)
                {
                    Vector3 pos = ConvertPositionToInternalRep(x, y, z);
                    float height = instanceTree.GetHeight(pos, maxSearchDist);
                    if (float.IsInfinity(height))
                        height = MapConst.VMAPInvalidHeightValue; // No height

                    return height;
                }
            }

            return MapConst.VMAPInvalidHeightValue;
        }



        public bool GetAreaAndLiquidData(int mapId, float x, float y, float z, byte? reqLiquidType, out AreaAndLiquidData data)
        {
            data = new AreaAndLiquidData();

            var instanceTree = iInstanceMapTrees.LookupByKey(mapId);
            if (instanceTree != null)
            {
                LocationInfo info = new();
                Vector3 pos = ConvertPositionToInternalRep(x, y, z);
                if (instanceTree.GetLocationInfo(pos, info))
                {
                    data.floorZ = info.ground_Z;

                    if (!Global.DisableMgr.IsVMAPDisabledFor(mapId, DisableFlags.VmapLiquidStatus))
                    {
                        int liquidType = info.hitModel.GetLiquidType(); // entry from LiquidType.dbc
                        float liquidLevel = 0;
                        if (!reqLiquidType.HasValue || (Global.DB2Mgr.GetLiquidFlags(liquidType) & reqLiquidType.Value) != 0)
                            if (info.hitInstance.GetLiquidLevel(pos, info, ref liquidLevel))
                                data.liquidInfo = new(liquidType, liquidLevel);
                    }

                    if (!Global.DisableMgr.IsVMAPDisabledFor(mapId, DisableFlags.VmapLiquidStatus))
                        data.areaInfo = new((int)info.hitModel.GetWmoID(), info.hitInstance.adtId, info.rootId, info.hitModel.GetMogpFlags(), info.hitInstance.Id);

                    return true;
                }
            }

            return false;
        }

        public WorldModel AcquireModelInstance(string filename)
        {
            lock (LoadedModelFilesLock)
            {
                filename = filename.TrimEnd('\0');
                var model = iLoadedModelFiles.LookupByKey(filename);
                if (model == null)
                {
                    model = new ManagedModel();
                    if (!model.GetModel().ReadFile(VMapPath + filename))
                    {
                        Log.outError(LogFilter.Server, "VMapManager: could not load '{0}'", filename);
                        return null;
                    }

                    Log.outDebug(LogFilter.Maps, "VMapManager: loading file '{0}'", filename);
                    iLoadedModelFiles.Add(filename, model);
                }
                model.IncRefCount();
                return model.GetModel();
            }
        }

        public void ReleaseModelInstance(string filename)
        {
            lock (LoadedModelFilesLock)
            {
                filename = filename.TrimEnd('\0');
                var model = iLoadedModelFiles.LookupByKey(filename);
                if (model == null)
                {
                    Log.outError(LogFilter.Server, "VMapManager: trying to unload non-loaded file '{0}'", filename);
                    return;
                }
                if (model.DecRefCount() == 0)
                {
                    Log.outDebug(LogFilter.Maps, "VMapManager: unloading file '{0}'", filename);
                    iLoadedModelFiles.Remove(filename);
                }
            }
        }

        public LoadResult ExistsMap(int mapId, int x, int y)
        {
            return StaticMapTree.CanLoadMap(VMapPath, mapId, x, y, this);
        }

        public int GetParentMapId(int mapId)
        {
            if (iParentMapData.ContainsKey(mapId))
                return iParentMapData[mapId];

            return -1;
        }

        Vector3 ConvertPositionToInternalRep(float x, float y, float z)
        {
            Vector3 pos = new();
            float mid = 0.5f * 64.0f * 533.33333333f;
            pos.X = mid - x;
            pos.Y = mid - y;
            pos.Z = z;

            return pos;
        }

        public static string GetTileFileName(int mapId, int tileX, int tileY, string extension)
        {
            return $"{mapId:D4}/{mapId:D4}_{tileX:D2}_{tileY:D2}.{extension}";
        }

        public static string GetMapFileName(int mapId)
        {
            return $"{mapId:D4}/{mapId:D4}.vmtree";
        }

        public void SetEnableLineOfSightCalc(bool pVal) { _enableLineOfSightCalc = pVal; }
        public void SetEnableHeightCalc(bool pVal) { _enableHeightCalc = pVal; }

        public bool IsLineOfSightCalcEnabled() { return _enableLineOfSightCalc; }
        public bool IsHeightCalcEnabled() { return _enableHeightCalc; }
        public bool IsMapLoadingEnabled() { return _enableLineOfSightCalc || _enableHeightCalc; }

        Dictionary<string, ManagedModel> iLoadedModelFiles = new();
        Dictionary<int, StaticMapTree> iInstanceMapTrees = new();
        Dictionary<int, int> iParentMapData = new();
        bool _enableLineOfSightCalc;
        bool _enableHeightCalc;

        object LoadedModelFilesLock = new();
    }

    public class ManagedModel
    {
        public ManagedModel()
        {
            iModel = new();
            iRefCount = 0;
        }

        public void SetModel(WorldModel model) { iModel = model; }
        public WorldModel GetModel() { return iModel; }
        public void IncRefCount() { ++iRefCount; }
        public int DecRefCount() { return --iRefCount; }

        WorldModel iModel;
        int iRefCount;
    }

    public class AreaAndLiquidData
    {
        public class AreaInfo
        {
            public int GroupId;
            public int AdtId;
            public int RootId;
            public uint MogpFlags;
            public uint UniqueId;

            public AreaInfo(int groupId, int adtId, int rootId, uint mogpFlags, uint uniqueId)
            {
                GroupId = groupId;
                AdtId = adtId;
                RootId = rootId;
                MogpFlags = mogpFlags;
                UniqueId = uniqueId;
            }
        }

        public class LiquidInfo
        {
            public int LiquidType;
            public float Level;

            public LiquidInfo(int type, float level)
            {
                LiquidType = type;
                Level = level;
            }
        }

        public float floorZ = MapConst.VMAPInvalidHeightValue;
        public AreaInfo areaInfo;
        public LiquidInfo liquidInfo;
    }
}
