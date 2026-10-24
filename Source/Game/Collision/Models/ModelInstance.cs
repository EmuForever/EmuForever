// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.GameMath;
using System;
using System.IO;
using System.Numerics;

namespace Game.Collision
{
    // WorldModel flags (stored in the .vmo WMOD chunk)
    public enum ModelFlags
    {
        M2 = 1
    }

    // ModelSpawn flags (stored per spawn in .vmtile)
    public enum ModelSpawnFlags
    {
        HasBound = 1 << 0,
        ParentSpawn = 1 << 1,
        PathOnly = 1 << 2
    }

    public class ModelMinimalData
    {
        public byte flags;
        public byte adtId;
        public uint Id;
        public Vector3 iPos;
        public float iScale;
        public AxisAlignedBox iBound;
        public string name;
    }

    public class ModelSpawn : ModelMinimalData
    {
        public Vector3 iRot;

        public ModelSpawn() { }

        public ModelSpawn(ModelSpawn spawn)
        {
            flags = spawn.flags;
            adtId = spawn.adtId;
            Id = spawn.Id;
            iPos = spawn.iPos;
            iRot = spawn.iRot;
            iScale = spawn.iScale;
            iBound = spawn.iBound;
            name = spawn.name;
        }

        public static bool ReadFromFile(BinaryReader reader, out ModelSpawn spawn)
        {
            spawn = new ModelSpawn();

            spawn.flags = reader.ReadByte();
            spawn.adtId = reader.ReadByte();
            spawn.Id = reader.ReadUInt32();
            spawn.iPos = reader.Read<Vector3>();
            spawn.iRot = reader.Read<Vector3>();
            spawn.iScale = reader.ReadSingle();

            bool has_bound = Convert.ToBoolean(spawn.flags & (uint)ModelSpawnFlags.HasBound);
            if (has_bound) // only WMOs have bound in MPQ, only available after computation
            {
                Vector3 bLow = reader.Read<Vector3>();
                Vector3 bHigh = reader.Read<Vector3>();
                spawn.iBound = new AxisAlignedBox(bLow, bHigh);
            }

            uint nameLen = reader.ReadUInt32();
            spawn.name = reader.ReadString((int)nameLen);
            return true;
        }
    }

    public class ModelInstance : ModelMinimalData
    {
        Matrix4x4 iInvRot;
        float iInvScale;
        WorldModel iModel;

        public ModelInstance()
        {
            iInvScale = 0.0f;
            iModel = null;
        }

        public ModelInstance(ModelSpawn spawn, WorldModel model)
        {
            flags = spawn.flags;
            adtId = spawn.adtId;
            Id = spawn.Id;
            iPos = spawn.iPos;
            iScale = spawn.iScale;
            iBound = spawn.iBound;
            name = spawn.name;

            iModel = model;

            Extensions.fromEulerAnglesZYX(MathFunctions.PI * spawn.iRot.Y / 180.0f, MathFunctions.PI * spawn.iRot.X / 180.0f, MathFunctions.PI * spawn.iRot.Z / 180.0f).Inverse(out iInvRot);

            iInvScale = 1.0f / iScale;
        }

        public bool MovesOutward(Vector3 from,Vector3 to)
        {
            var center = (iBound.Lo+iBound.Hi)/2;
            Vector2 offset = new(from.X-center.X,from.Y-center.Y), delta = new(to.X-from.X,to.Y-from.Y);
            if (Vector2.Dot(offset,delta) < -.001f) return false;
            if (from.X < iBound.Lo.X && to.X > from.X+.001f || from.X > iBound.Hi.X && to.X < from.X-.001f ||
                from.Y < iBound.Lo.Y && to.Y > from.Y+.001f || from.Y > iBound.Hi.Y && to.Y < from.Y-.001f) return false;
            return true;
        }

        public bool ContainsSmallSolid(Vector3 point)
        {
            Vector3 size = iBound.Hi - iBound.Lo;
            if (iModel == null || !float.IsFinite(iScale) || iScale <= 0 || (iModel.Flags & (uint)ModelFlags.M2) == 0 || !iBound.contains(point) ||
                size.X > 6 || size.Y > 6 || size.Z > 4 || size.X <= 0 || size.Y <= 0 || size.Z <= 0) return false;
            // An enclosing bounding box is insufficient: require actual collision
            // geometry in both directions on every axis. Rooms and large WMOs
            // are never eligible for this initial-overlap recovery.
            foreach (Vector3 direction in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
            {
                float distance = 8;
                if (!IntersectRay(new Ray(point, direction), ref distance, false, ModelIgnoreFlags.Nothing)) return false;
            }
            return true;
        }

        public bool IntersectRay(Ray pRay, ref float pMaxDist, bool pStopAtFirstHit, ModelIgnoreFlags ignoreFlags)
        {
            if (iModel == null)
                return false;

            float time = pRay.intersectionTime(iBound);
            if (float.IsInfinity(time))
                return false;

            // child bounds are defined in object space:
            Vector3 p = iInvRot.Multiply(pRay.Origin - iPos) * iInvScale;
            Ray modRay = new Ray(p, iInvRot.Multiply(pRay.Direction));
            float distance = pMaxDist * iInvScale;
            bool hit = iModel.IntersectRay(modRay, ref distance, pStopAtFirstHit, ignoreFlags);
            if (hit)
            {
                distance *= iScale;
                pMaxDist = distance;
            }
            return hit;
        }

        public bool GetLiquidLevel(Vector3 p, LocationInfo info, ref float liqHeight)
        {
            // child bounds are defined in object space:
            Vector3 pModel = iInvRot.Multiply(p - iPos) * iInvScale;
            //Vector3 zDirModel = iInvRot * Vector3(0.f, 0.f, -1.f);
            float zDist;
            if (info.hitModel.GetLiquidLevel(pModel, out zDist))
            {
                // calculate world height (zDist in model coords):
                // assume WMO not tilted (wouldn't make much sense anyway)
                liqHeight = zDist * iScale + iPos.Z;
                return true;
            }
            return false;
        }

        public bool GetLocationInfo(Vector3 p, LocationInfo info)
        {
            if (iModel == null)
                return false;

            // M2 files don't contain area info, only WMO files
            if ((iModel.Flags & (uint)ModelFlags.M2) != 0)
                return false;
            if (!iBound.contains(p))
                return false;
            // child bounds are defined in object space:
            Vector3 pModel = iInvRot.Multiply(p - iPos) * iInvScale;
            Vector3 zDirModel = iInvRot.Multiply(new Vector3(0.0f, 0.0f, -1.0f));
            float zDist;

            GroupLocationInfo groupInfo = new();
            if (iModel.GetLocationInfo(pModel, zDirModel, out zDist, groupInfo))
            {
                Vector3 modelGround = pModel + zDist * zDirModel;
                // Transform back to world space. Note that:
                // Mat * vec == vec * Mat.transpose()
                // and for rotation matrices: Mat.inverse() == Mat.transpose()
                float world_Z = (iInvRot.Multiply(modelGround * iScale) + iPos).Z;
                if (info.ground_Z < world_Z) // hm...could it be handled automatically with zDist at intersection?
                {
                    info.rootId = groupInfo.rootId;
                    info.hitModel = groupInfo.hitModel;
                    info.ground_Z = world_Z;
                    info.hitInstance = this;
                    return true;
                }
            }
            return false;
        }

        public void SetUnloaded() { iModel = null; }
        public WorldModel GetWorldModel() { return iModel; }
    }
}
