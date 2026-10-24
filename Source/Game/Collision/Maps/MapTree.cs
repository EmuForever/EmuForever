// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.GameMath;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Game.Collision
{
    public class GroupLocationInfo
    {
        public GroupModel hitModel;
        public int rootId = -1;
    }

    public class LocationInfo
    {
        public LocationInfo()
        {
            ground_Z = float.NegativeInfinity;
        }

        public int rootId;
        public ModelInstance hitInstance;
        public GroupModel hitModel;
        public float ground_Z;
    }

    public class AreaInfo
    {
        public AreaInfo()
        {
            ground_Z = float.NegativeInfinity;
        }

        public bool result;
        public float ground_Z;
        public uint flags;
        public int adtId;
        public int rootId;
        public int groupId;
    }

    public class StaticMapTree
    {
        public StaticMapTree(int mapId)
        {
            iMapID = mapId;
        }

        public LoadResult InitMap(string fname)
        {
            Log.outDebug(LogFilter.Maps,
                $"StaticMapTree.InitMap() : initializing StaticMapTree '{fname}'");

            if (!File.Exists(fname))
                return LoadResult.FileNotFound;

            using (BinaryReader reader = new(new FileStream(fname, FileMode.Open, FileAccess.Read)))
            {
                var magic = reader.ReadStringFromChars(8);
                if (magic != MapConst.VMapMagic)
                    return LoadResult.VersionMismatch;

                var node = reader.ReadStringFromChars(4);
                if (node != "NODE")
                    return LoadResult.ReadFromFileFailed;

                if (!iTree.ReadFromFile(reader))
                    return LoadResult.ReadFromFileFailed;

                iNTreeValues = iTree.PrimCount();
                iTreeValues = new ModelInstance[iNTreeValues];
                iTreeValueRefs = new uint[iNTreeValues];
            }

            return LoadResult.Success;
        }

        public void UnloadMap(VMapManager vm)
        {
            if (iTreeValues != null)
            {
                for (uint i = 0; i < iNTreeValues; ++i)
                {
                    if (iTreeValueRefs[i] == 0 || iTreeValues[i] == null)
                        continue;

                    vm.ReleaseModelInstance(iTreeValues[i].name);
                    iTreeValues[i].SetUnloaded();
                    iTreeValueRefs[i] = 0;
                }
            }
            iLoadedTiles.Clear();
        }

        public LoadResult LoadMapTile(int tileX, int tileY, VMapManager vm)
        {
            if (iTreeValues == null)
            {
                Log.outError(LogFilter.Server,
                    $"StaticMapTree.LoadMapTile() : " +
                    $"tree has not been initialized [{tileX}, {tileY}]");

                return LoadResult.ReadFromFileFailed;
            }

            LoadResult result = LoadResult.FileNotFound;

            List<uint> tileReferenceVals = new();
            iLoadedTiles[PackTileID(tileX, tileY)] = tileReferenceVals;

            TileFileOpenResult fileResult = OpenMapTileFile(VMapManager.VMapPath, iMapID, tileX, tileY, vm);
            if (fileResult.File != null && fileResult.SpawnIndicesFile != null)
            {
                result = LoadResult.Success;
                using BinaryReader reader = new(fileResult.File);
                using BinaryReader idxReader = new(fileResult.SpawnIndicesFile);
                if (reader.ReadStringFromChars(8) != MapConst.VMapMagic)
                    result = LoadResult.VersionMismatch;
                if (idxReader.ReadStringFromChars(8) != MapConst.VMapMagic)
                    result = LoadResult.VersionMismatch;

                uint numSpawns = 0;
                if (result == LoadResult.Success)
                {
                    numSpawns = reader.ReadUInt32();
                    if (idxReader.ReadUInt32() != numSpawns)
                        result = LoadResult.ReadFromFileFailed;
                }

                for (uint i = 0; i < numSpawns && result == LoadResult.Success; ++i)
                {
                    // read model spawns
                    if (!ModelSpawn.ReadFromFile(reader, out ModelSpawn spawn))
                    {
                        Log.outError(LogFilter.Maps,
                            $"StaticMapTree.LoadMapTile() : cannot read model from file " +
                            $"(spawn index {i}) referenced in tile {fileResult.Name} by map {iMapID}");
                        result = LoadResult.ReadFromFileFailed;
                        break;
                    }

                    uint referencedVal = idxReader.ReadUInt32();
                    if (referencedVal >= iNTreeValues)
                    {
                        Log.outError(LogFilter.Maps,
                            $"StaticMapTree.LoadMapTile() : " +
                            $"invalid tree element ({referencedVal}/{iNTreeValues}) " +
                            $"referenced in tile {fileResult.Name}");
                        result = LoadResult.ReadFromFileFailed;
                        break;
                    }

                    if ((spawn.flags & (uint)ModelSpawnFlags.PathOnly) != 0)
                        continue;

                    if (iTreeValueRefs[referencedVal] == 0)
                    {
                        // acquire model instance
                        WorldModel model = vm.AcquireModelInstance(spawn.name);
                        if (model == null)
                        {
                            Log.outError(LogFilter.Server,
                                $"StaticMapTree.LoadMapTile() : " +
                                $"could not acquire WorldModel [{tileX}, {tileY}]");
                            continue;
                        }

                        iTreeValues[referencedVal] = new ModelInstance(spawn, model);
                    }

                    ++iTreeValueRefs[referencedVal];
                    tileReferenceVals.Add(referencedVal);
                }
            }

            fileResult.File?.Dispose();
            fileResult.SpawnIndicesFile?.Dispose();
            return result;
        }

        public void UnloadMapTile(int tileX, int tileY, VMapManager vm)
        {
            uint tileID = PackTileID(tileX, tileY);
            if (!iLoadedTiles.Remove(tileID, out List<uint> tileReferenceVals))
            {
                Log.outError(LogFilter.Server,
                    $"StaticMapTree.UnloadMapTile() : " +
                    $"trying to unload non-loaded tile - Map:{iMapID} X:{tileX} Y:{tileY}");
                return;
            }

            foreach (uint referencedVal in tileReferenceVals)
            {
                if (iTreeValueRefs[referencedVal] == 0 || iTreeValues[referencedVal] == null)
                {
                    Log.outError(LogFilter.Server,
                        $"StaticMapTree.UnloadMapTile() : " +
                        $"trying to unload non-referenced model (tree element {referencedVal}) - Map:{iMapID} X:{tileX} Y:{tileY}");
                    continue;
                }

                if (--iTreeValueRefs[referencedVal] == 0)
                {
                    vm.ReleaseModelInstance(iTreeValues[referencedVal].name);
                    iTreeValues[referencedVal].SetUnloaded();
                }
            }
        }

        static uint PackTileID(int tileX, int tileY) { return (uint)(tileX << 16 | tileY); }
        static void UnpackTileID(int ID, ref int tileX, ref int tileY) { tileX = ID >> 16; tileY = ID & 0xFF; }

        static TileFileOpenResult OpenMapTileFile(string vmapPath, int mapID, int tileX, int tileY, VMapManager vm)
        {
            TileFileOpenResult result = new();
            result.Name = vmapPath + VMapManager.GetTileFileName(mapID, tileX, tileY, "vmtile");
            result.UsedMapId = mapID;

            string idxName = vmapPath + VMapManager.GetTileFileName(mapID, tileX, tileY, "vmtileidx");
            if (File.Exists(idxName))
                result.SpawnIndicesFile = new FileStream(idxName, FileMode.Open, FileAccess.Read);

            if (File.Exists(result.Name))
            {
                result.File = new FileStream(result.Name, FileMode.Open, FileAccess.Read);
                return result;
            }

            int parentMapId = vm.GetParentMapId(mapID);
            while (parentMapId != -1)
            {
                result.Name = vmapPath + VMapManager.GetTileFileName(parentMapId, tileX, tileY, "vmtile");
                if (File.Exists(result.Name))
                {
                    result.File = new FileStream(result.Name, FileMode.Open, FileAccess.Read);
                    result.UsedMapId = parentMapId;
                    return result;
                }

                parentMapId = vm.GetParentMapId(parentMapId);
            }

            return result;
        }

        public static LoadResult CanLoadMap(string vmapPath, int mapID, int tileX, int tileY, VMapManager vm)
        {
            string fullname = vmapPath + VMapManager.GetMapFileName(mapID);
            if (!File.Exists(fullname))
                return LoadResult.FileNotFound;

            using (BinaryReader reader = new(new FileStream(fullname, FileMode.Open, FileAccess.Read)))
            {
                if (reader.ReadStringFromChars(8) != MapConst.VMapMagic)
                    return LoadResult.VersionMismatch;
            }

            TileFileOpenResult fileResult = OpenMapTileFile(vmapPath, mapID, tileX, tileY, vm);
            if (fileResult.File == null || fileResult.SpawnIndicesFile == null)
            {
                fileResult.File?.Dispose();
                fileResult.SpawnIndicesFile?.Dispose();
                return LoadResult.FileNotFound;
            }

            fileResult.SpawnIndicesFile.Dispose();
            using (BinaryReader reader = new(fileResult.File))
            {
                if (reader.ReadStringFromChars(8) != MapConst.VMapMagic)
                    return LoadResult.VersionMismatch;
            }

            return LoadResult.Success;
        }

        public bool GetLocationInfo(Vector3 pos, LocationInfo info)
        {
            LocationInfoCallback intersectionCallBack = new(iTreeValues, info);
            iTree.IntersectPoint(pos, intersectionCallBack);
            return intersectionCallBack.result;
        }

        public float GetHeight(Vector3 pPos, float maxSearchDist)
        {
            float height = float.PositiveInfinity;
            Vector3 dir = new(0, 0, -1);
            Ray ray = new(pPos, dir);   // direction with length of 1
            float maxDist = maxSearchDist;
            if (GetIntersectionTime(ray, ref maxDist, false, ModelIgnoreFlags.Nothing))
                height = pPos.Z - maxDist;

            return height;
        }
        bool GetIntersectionTime(Ray pRay, ref float pMaxDist, bool pStopAtFirstHit, ModelIgnoreFlags ignoreFlags, IReadOnlySet<uint> ignored = null)
        {
            float distance = pMaxDist;
            MapRayCallback intersectionCallBack = new(iTreeValues, ignoreFlags, ignored);
            iTree.IntersectRay(pRay, intersectionCallBack, ref distance, pStopAtFirstHit);
            if (intersectionCallBack.DidHit())
                pMaxDist = distance;
            return intersectionCallBack.DidHit();
        }

        public bool GetObjectHitPos(Vector3 pPos1, Vector3 pPos2, out Vector3 pResultHitPos, float pModifyDist)
        {
            bool result;
            float maxDist = (pPos2 - pPos1).Length();
            // valid map coords should *never ever* produce float overflow, but this would produce NaNs too
            Cypher.Assert(maxDist < float.MaxValue);
            // prevent NaN values which can cause BIH intersection to enter infinite loop
            if (maxDist < 1e-10f)
            {
                pResultHitPos = pPos2;
                return false;
            }
            Vector3 dir = (pPos2 - pPos1) / maxDist;              // direction with length of 1
            Ray ray = new(pPos1, dir);
            float dist = maxDist;
            if (GetIntersectionTime(ray, ref dist, false, ModelIgnoreFlags.Nothing))
            {
                pResultHitPos = pPos1 + dir * dist;
                if (pModifyDist < 0)
                {
                    if ((pResultHitPos - pPos1).Length() > -pModifyDist)
                    {
                        pResultHitPos += dir * pModifyDist;
                    }
                    else
                    {
                        pResultHitPos = pPos1;
                    }
                }
                else
                {
                    pResultHitPos += dir * pModifyDist;
                }
                result = true;
            }
            else
            {
                pResultHitPos = pPos2;
                result = false;
            }
            return result;
        }

        public List<ModelInstance> GetEmbeddedSmallModels(Vector3 point)
        {
            var callback = new EmbeddedSolidCallback(iTreeValues);
            iTree.IntersectPoint(point,callback);
            for (int sample = 0; sample < 8; ++sample)
            {
                float angle = sample * MathF.PI / 4;
                iTree.IntersectPoint(point + new Vector3(MathF.Cos(angle)*.25f,MathF.Sin(angle)*.25f,0),callback);
            }
            return callback.Models;
        }

        // The nearest static model crossed by the segment, for callers that must
        // describe the obstacle rather than only learn that sight is blocked.
        public ModelInstance GetFirstHit(Vector3 pos1, Vector3 pos2, IReadOnlySet<uint> ignored)
        {
            float maxDist = (pos2 - pos1).Length();
            if (!(maxDist > 1e-10f) || !float.IsFinite(maxDist))
                return null;
            var callback = new FirstHitCallback(iTreeValues, ignored);
            iTree.IntersectRay(new Ray(pos1, (pos2 - pos1) / maxDist), callback, ref maxDist, false);
            return callback.Hit;
        }

        public bool IsInLineOfSight(Vector3 pos1, Vector3 pos2, ModelIgnoreFlags ignoreFlags) => IsInLineOfSight(pos1, pos2, ignoreFlags, null);
        public bool IsInLineOfSight(Vector3 pos1, Vector3 pos2, ModelIgnoreFlags ignoreFlags, IReadOnlySet<uint> ignored)
        {
            float maxDist = (pos2 - pos1).Length();
            // return false if distance is over max float, in case of cheater teleporting to the end of the universe
            if (maxDist == float.MaxValue ||
                maxDist == float.PositiveInfinity)
                return false;

            // valid map coords should *never ever* produce float overflow, but this would produce NaNs too
            Cypher.Assert(maxDist < float.MaxValue);
            // prevent NaN values which can cause BIH intersection to enter infinite loop
            if (maxDist < 1e-10f)
                return true;
            // direction with length of 1
            Ray ray = new(pos1, (pos2 - pos1) / maxDist);
            if (GetIntersectionTime(ray, ref maxDist, true, ignoreFlags, ignored))
                return false;

            return true;
        }

        public int NumLoadedTiles() { return iLoadedTiles.Count; }

        int iMapID;
        BIH iTree = new();
        ModelInstance[] iTreeValues;
        uint iNTreeValues;
        uint[] iTreeValueRefs;

        Dictionary<uint, List<uint>> iLoadedTiles = new();
    }

    class TileFileOpenResult
    {
        public string Name;
        public FileStream File;
        public FileStream SpawnIndicesFile;
        public int UsedMapId;
    }
}
