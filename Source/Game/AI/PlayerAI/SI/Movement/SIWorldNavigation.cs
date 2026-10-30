using Framework.Constants;
using Game.Movement;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Game.AI.SIMovement;

// SI-only Detour filter. Ground that the mesh marks too steep for players is never walkable.
// Hostile aggro circles are a cost, never a hard rejection: a route that
// has to pass a camp is still a route.
public sealed class SINavFilter : Detour.dtQueryFilter
{
    public ImmutableArray<SITravelThreat> Threats{get;private set;}=[];
    public Vector3 Origin{get;private set;}
    const float ThreatCost=150;
    float[] _threatData=[];
    // The cost itself is applied by the prebuilt native Detour threat filter, with the same
    // geometry as SITravelThreat.Blocks; per-edge callbacks into managed code would cost more than the search.
    public void Use(ImmutableArray<SITravelThreat> threats,Vector3 origin)
    {
        Threats=threats.IsDefault?[]:threats;Origin=origin;
        if(_threatData.Length<Threats.Length*4)_threatData=new float[Threats.Length*4];
        for(int i=0;i<Threats.Length;++i)
        {
            var threat=Threats[i];
            _threatData[i*4]=threat.Position.X;_threatData[i*4+1]=threat.Position.Y;_threatData[i*4+2]=threat.Position.Z;_threatData[i*4+3]=threat.Radius;
        }
        setThreats(_threatData,Threats.Length,[origin.X,origin.Y,origin.Z],ThreatCost);
    }
    public SINavFilter()
    {
        setIncludeFlags((ushort)(NavTerrainFlag.Ground|NavTerrainFlag.Water));
        setExcludeFlags((ushort)(NavTerrainFlag.MagmaSlime|NavTerrainFlag.GroundSteep));
        setAreaCost((int)NavArea.Ground,1);
        setAreaCost((int)NavArea.GroundSteep,4);
        setAreaCost((int)NavArea.Water,2);
    }
}

// A worker owns its planner: the mesh, tile cache and query are never shared with a map thread
// or with another worker.
public sealed class SINavMeshPlanner
{
    sealed class Scene
    {
        internal Detour.dtNavMesh Mesh;internal Detour.dtNavMeshQuery Query;
        // Small search budget for link checks: a failed link must not flood a whole island.
        internal Detour.dtNavMeshQuery LinkQuery;
        internal Dictionary<(int,int),TileEntry> Tiles=new();
        internal long LastUse;
    }
    sealed record TileEntry(ulong Reference,int FileBytes,long LastUse);
    // Tiles live in native Detour memory at about their file size (~0.5 MB average). Each worker
    // keeps its own copy, so the budget stays modest; one query needs at most 36 tiles.
    const int MaximumScenes=32,MaximumResidentTiles=128,MaximumNodes=65535,LinkNodes=3072;
    const long MaximumResidentFileBytes=64L*1024*1024;
    readonly string _data;
    readonly Dictionary<int,Scene> _scenes=new();
    public readonly SINavFilter Filter=new();
    long _use,_loads,_evictions,_residentBytes;
    int _residentTiles;
    public (long Loads,long Evictions,int Tiles,long FileBytes) CacheStatistics=>(_loads,_evictions,_residentTiles,_residentBytes);
    public SINavMeshPlanner(string data){_data=Path.GetFullPath(data);}

    public static bool HasMesh(string data,int map)=>File.Exists(Path.Combine(data,"mmaps",$"{map:D4}.mmap"));
    Scene Open(int map)
    {
        if(_scenes.TryGetValue(map,out var scene))return scene;
        string file=Path.Combine(_data,"mmaps",$"{map:D4}.mmap");
        if(!File.Exists(file))throw new InvalidDataException("No navigation mesh for this map");
        using var reader=new BinaryReader(File.OpenRead(file));
        if(reader.BaseStream.Length>=40&&reader.ReadUInt32()==MapConst.mmapMagic)
        { uint version=reader.ReadUInt32();if(version!=MapConst.mmapVersion&&version!=MapConst.mmapLegacyVersion)throw new InvalidDataException("Unsupported mmap version"); }
        else reader.BaseStream.Position=0;
        var p=new Detour.dtNavMeshParams();
        for(int i=0;i<3;++i)p.orig[i]=reader.ReadSingle();
        p.tileWidth=reader.ReadSingle();p.tileHeight=reader.ReadSingle();p.maxTiles=reader.ReadInt32();p.maxPolys=reader.ReadInt32();
        // The installed 64-bit Detour format stores 1U<<31 in the signed maxPolys
        // metadata field. It is a reference bit budget, not an array allocation.
        if(p.maxTiles<1||p.maxTiles>65536||p.maxPolys==0)throw new InvalidDataException("Invalid mmap capacity");
        var mesh=new Detour.dtNavMesh(); if(Detour.dtStatusFailed(mesh.init(p)))throw new InvalidDataException("Cannot initialize private mesh");
        var query=new Detour.dtNavMeshQuery(); if(Detour.dtStatusFailed(query.init(mesh,MaximumNodes)))throw new InvalidDataException("Cannot initialize private query");
        var linkQuery=new Detour.dtNavMeshQuery(); if(Detour.dtStatusFailed(linkQuery.init(mesh,LinkNodes)))throw new InvalidDataException("Cannot initialize private query");
        if(_scenes.Count>=MaximumScenes)
        {
            var oldest=_scenes.MinBy(pair=>pair.Value.LastUse);
            foreach(var old in oldest.Value.Tiles.Values){_residentBytes-=old.FileBytes;--_residentTiles;++_evictions;}
            _scenes.Remove(oldest.Key);
        }
        scene=new(){Mesh=mesh,Query=query,LinkQuery=linkQuery,LastUse=++_use};_scenes.Add(map,scene);return scene;
    }
    public string TileFile(int map,int x,int y)
    {
        string modern=Path.Combine(_data,"mmaps",$"{map:D4}_{x:D2}_{y:D2}.mmtile");
        return File.Exists(modern)?modern:Path.Combine(_data,"mmaps",$"{map:D4}{x:D2}{y:D2}.mmtile");
    }
    Detour.dtMeshTile Load(Scene scene,int map,int x,int y,HashSet<(int,int)> protectedTiles)
    {
        if(scene.Tiles.TryGetValue((x,y),out var existing))
        {scene.Tiles[(x,y)]=existing with{LastUse=++_use};return scene.Mesh.getTileByRef(existing.Reference);}
        string file=TileFile(map,x,y);if(!File.Exists(file))return null;
        using var reader=new BinaryReader(File.OpenRead(file));
        var header=reader.Read<MmapTileHeader>();
        if(header.mmapMagic!=MapConst.mmapMagic || header.mmapVersion!=MapConst.mmapVersion&&header.mmapVersion!=MapConst.mmapLegacyVersion ||
            header.size>64*1024*1024 || header.size>reader.BaseStream.Length-reader.BaseStream.Position)throw new InvalidDataException("Invalid mmap tile");
        while(_residentTiles>=MaximumResidentTiles||_residentBytes+header.size>MaximumResidentFileBytes)
        {
            Scene victim=null;(int,int) key=default;TileEntry oldest=null;
            foreach(var candidateScene in _scenes.Values)foreach(var candidate in candidateScene.Tiles)
                if((candidateScene!=scene||!protectedTiles.Contains(candidate.Key))&&(oldest==null||candidate.Value.LastUse<oldest.LastUse))
                {victim=candidateScene;key=candidate.Key;oldest=candidate.Value;}
            if(oldest==null)break; // Every resident tile is needed by this query; exceed the soft bound once.
            victim.Mesh.removeTile(oldest.Reference,out _);victim.Tiles.Remove(key);
            _residentBytes-=oldest.FileBytes;--_residentTiles;++_evictions;
        }
        var raw=new Detour.dtRawTileData();raw.FromBytes(reader.ReadBytes((int)header.size),0);
        ulong reference=0;
        if(Detour.dtStatusFailed(scene.Mesh.addTile(raw,0,0,ref reference)))throw new InvalidDataException("Cannot attach private mmap tile");
        scene.Tiles.Add((x,y),new(reference,(int)header.size,++_use));
        _residentBytes+=header.size;++_residentTiles;++_loads;
        return scene.Mesh.getTileByRef(reference);
    }
    public static (int X,int Y) Tile(Vector3 p)=>((int)(MapConst.CenterGridId-p.X/MapConst.SizeofGrids),(int)(MapConst.CenterGridId-p.Y/MapConst.SizeofGrids));
    public static float[] Mesh(Vector3 p)=>[p.Y,p.Z,p.X];
    public static Vector3 World(float[] p)=>new(p[2],p[0],p[1]);
    public static HashSet<(int,int)> RequiredTiles(Vector3 a,Vector3 b,float margin)
    {
        var from=Tile(new(Math.Min(a.X,b.X)-margin,Math.Min(a.Y,b.Y)-margin,0));
        var to=Tile(new(Math.Max(a.X,b.X)+margin,Math.Max(a.Y,b.Y)+margin,0));
        var needed=new HashSet<(int,int)>();
        for(int x=Math.Max(0,Math.Min(from.X,to.X));x<=Math.Min(63,Math.Max(from.X,to.X));++x)
            for(int y=Math.Max(0,Math.Min(from.Y,to.Y));y<=Math.Min(63,Math.Max(from.Y,to.Y));++y)needed.Add((x,y));
        return needed;
    }
    Scene Prepare(int map,Vector3 a,Vector3 b,float margin)
    {
        var scene=Open(map);scene.LastUse=++_use;var needed=RequiredTiles(a,b,margin);
        if(needed.Count>36)needed=RequiredTiles(a,b,0);
        if(needed.Count>36)throw new InvalidDataException("Local tile window exceeded");
        foreach(var (x,y) in needed)Load(scene,map,x,y,needed);
        return scene;
    }

    // Surface levels found in a vertical column, for the travel graph's node sampling.
    public List<Vector3> SurfaceLevels(int map,int tileX,int tileY,Vector3 center,float halfWidth)
    {
        var scene=Open(map);scene.LastUse=++_use;
        var tile=Load(scene,map,tileX,tileY,[(tileX,tileY)]);
        var levels=new List<Vector3>();
        if(tile?.header==null)return levels;
        float low=tile.header.bmin[1]-2,high=tile.header.bmax[1]+2;
        for(float z=high;z>=low-15;z-=24)
        {
            ulong poly=0;var nearest=new float[3];
            var point=new Vector3(center.X,center.Y,z);
            if(Detour.dtStatusFailed(scene.Query.findNearestPoly(Mesh(point),[halfWidth,13,halfWidth],Filter,ref poly,ref nearest))||poly==0)continue;
            byte area=0;scene.Mesh.getPolyArea(poly,ref area);
            if(area==(byte)NavArea.Water)continue;
            var found=World(nearest);
            if(levels.All(level=>Math.Abs(level.Z-found.Z)>8||Vector2.Distance(new(level.X,level.Y),new(found.X,found.Y))>halfWidth*1.5f))levels.Add(found);
            if(levels.Count>=6)break;
        }
        return levels;
    }

    // Nearest mesh point and polygon, searching outward. A bot on a prop, a corpse in a doorway or an NPC
    // behind a counter is resolved to the closest walkable floor rather than rejected.
    (ulong Poly,Vector3 Point) Snap(Scene scene,Vector3 point,bool heightKnown,float slack)
    {
        var center=Mesh(point);
        (float H,float V)[] steps=heightKnown?[(1.5f,3),(4,8),(Math.Max(10,slack),20),(Math.Max(25,slack),60)]:[(3,500),(12,500),(Math.Max(30,slack),500)];
        foreach(var (h,v) in steps)
        {
            ulong poly=0;var nearest=new float[3];
            if(Detour.dtStatusFailed(scene.Query.findNearestPoly(center,[h,v,h],Filter,ref poly,ref nearest))||poly==0)continue;
            return (poly,World(nearest));
        }
        return (0,default);
    }

    bool Water(Scene scene,ulong poly){byte area=0;scene.Mesh.getPolyArea(poly,ref area);return area==(byte)NavArea.Water;}

    // Walkable length between two points, or -1 when not connected within the limit. Used to verify
    // travel graph edges; cheaper than a full smoothed path.
    public float Connect(int map,Vector3 a,Vector3 b,float maximumLength,out bool water)
    {
        water=false;
        var scene=Prepare(map,a,b,Math.Max(30,maximumLength*.5f-Vector2.Distance(new(a.X,a.Y),new(b.X,b.Y))*.5f));
        var (pa,na)=Snap(scene,a,true,0);var (pb,nb)=Snap(scene,b,true,0);
        if(pa==0||pb==0||Vector3.Distance(na,a)>3||Vector3.Distance(nb,b)>3)return -1;
        var polys=new ulong[2048];uint count=0;
        uint status=scene.LinkQuery.findPath(pa,pb,Mesh(na),Mesh(nb),Filter,polys,ref count,polys.Length);
        if(Detour.dtStatusFailed(status)||count==0||polys[count-1]!=pb||(status&Detour.DT_PARTIAL_RESULT)!=0)return -1;
        for(int i=0;i<count;++i)if(Water(scene,polys[i])){water=true;break;}
        float[] straight=new float[256*3];byte[] flags=new byte[256];ulong[] refs=new ulong[256];int corners=0;
        status=scene.LinkQuery.findStraightPath(Mesh(na),Mesh(nb),polys,(int)count,straight,flags,refs,ref corners,256,0);
        if(Detour.dtStatusFailed(status)||corners<1)return -1;
        float length=Vector3.Distance(a,na);var previous=na;
        for(int i=0;i<corners;++i){var corner=new Vector3(straight[i*3+2],straight[i*3],straight[i*3+1]);length+=Vector3.Distance(previous,corner);previous=corner;}
        if(length>maximumLength)return -1;
        // A funnel alone can hide a steep detail triangle between its corners.
        // Graph connectivity must obey the same walking checks as actual legs.
        var surface=Smooth(scene,na,nb,polys,(int)count,()=>false,maximumLength,out bool reached);
        if(surface==null)surface=Corners(scene,na,nb,polys,(int)count,maximumLength,out reached);
        return reached&&SurfaceWalkable(scene,surface,out _,out _,Water(scene,pa))?length:-1;
    }

    // Plan far enough to see around terrain, but publish shorter legs so moving targets, threats and
    // task changes are picked up; the controller prefetches the next leg from this one's end.
    public const float LegLength=120;

    // A disconnected walking route stays disconnected; retrying with steep terrain
    // allowed made the server spline climb slopes a client-controlled player slides off.
    public SIGroundPath Plan(SIPathPosition from,SIPathPosition to,Func<bool> cancelled,float limit,float legLength=LegLength)
        => PlanOnFilter(from,to,cancelled,limit,legLength);

    // A corridor rejected for a too-steep stretch is searched again with that spot made expensive, so Detour
    // finds the road around a hillside instead of the bot climbing it. Mesh polygons only know "steep" above
    // 55 degrees, while players stop walking at about 50. With no gentler way, the route still fails.
    const int SteepDetours=3;
    const float SteepAvoidRadius=5;
    Vector3? _steepAt;
    SIGroundPath PlanOnFilter(SIPathPosition from,SIPathPosition to,Func<bool> cancelled,float limit,float legLength)
    {
        var baseline=Filter.Threats;var origin=Filter.Origin;var avoided=ImmutableArray.CreateBuilder<SITravelThreat>();
        try
        {
            for(int attempt=0;;++attempt)
            {
                _steepAt=null;
                var path=PlanCorridor(from,to,cancelled,limit,legLength);
                if(path.Success||_steepAt is not Vector3 at||attempt>=SteepDetours)return path;
                avoided.Add(new SITravelThreat(at,SteepAvoidRadius,false));
                Filter.Use(baseline.AddRange(avoided),origin);
            }
        }
        finally{if(avoided.Count>0)Filter.Use(baseline,origin);}
    }

    SIGroundPath PlanCorridor(SIPathPosition from,SIPathPosition to,Func<bool> cancelled,float limit,float legLength)
    {
        if(!from.Valid||!to.Valid||from.MapId!=to.MapId)return SIGroundPath.Failed("Destination is on another map");
        float flat=Vector2.Distance(new(from.Point.X,from.Point.Y),new(to.Point.X,to.Point.Y));
        if(flat>limit)return SIGroundPath.Failed("Local query exceeds configured distance");
        if(cancelled())throw new OperationCanceledException();
        // The window must hold the detour, not just the endpoints: a river or a cliff can need a long way round.
        var scene=Prepare(from.MapId,from.Point,to.Point,Math.Clamp(flat*.75f,60,300));
        var query=scene.Query;
        var (pa,startPoint)=Snap(scene,from.Point,true,0);
        if(pa==0)return SIGroundPath.Failed("No walkable ground near the start");
        var goal=to.Point;if(!to.HeightKnown)goal.Z=from.Point.Z;
        var (pb,endPoint)=Snap(scene,goal,to.HeightKnown,to.Slack);
        if(pb==0)return SIGroundPath.Failed("No walkable ground near the destination");
        var polys=new ulong[4096];int count=0;
        uint status=query.initSlicedFindPath(pa,pb,Mesh(startPoint),Mesh(endPoint),Filter,0);int iterations=0;
        while(Detour.dtStatusInProgress(status)&&iterations<MaximumNodes)
        { if(cancelled())throw new OperationCanceledException();int done=0;status=query.updateSlicedFindPath(256,ref done);iterations+=Math.Max(1,done); }
        // Out of search budget, this still returns the corridor to the best node found so far, marked partial.
        bool exhausted=Detour.dtStatusInProgress(status);
        status=query.finalizeSlicedFindPath(polys,ref count,polys.Length);
        if(exhausted)status|=Detour.DT_PARTIAL_RESULT;
        if(Detour.dtStatusFailed(status)||count==0)return SIGroundPath.Failed("No walkable route");
        bool partial=(status&Detour.DT_PARTIAL_RESULT)!=0||polys[count-1]!=pb;
        var target=endPoint;
        if(partial)
        {
            bool over=false;var closest=new float[3];
            if(Detour.dtStatusFailed(query.closestPointOnPoly(polys[count-1],Mesh(goal),closest,ref over)))return SIGroundPath.Failed("No walkable route");
            target=World(closest);
        }
        var points=Smooth(scene,startPoint,target,polys,count,cancelled,legLength,out bool reached);
        if(points==null)points=Corners(scene,startPoint,target,polys,count,legLength,out reached);
        if(points.Count==0)return SIGroundPath.Failed("No walkable route");
        var route=new List<Vector3>{from.Point};
        foreach(var point in points)
        {
            if(route.Count==1 && point==startPoint && SIWalkingSurface.InitialFloorCorrection(from.Point,point))continue;
            if(Vector3.DistanceSquared(route[^1],point)>.01f)route.Add(point);
        }
        if(route.Count<2)
        {
            // Already standing on the closest reachable point.
            if(!partial)return new([from.Point,from.Point]){Complete=true};
            return SIGroundPath.Failed(SIGroundPath.UnreachableError);
        }
        if(partial&&reached&&Vector2.Distance(new(route[^1].X,route[^1].Y),new(from.Point.X,from.Point.Y))<2)
            return SIGroundPath.Failed(SIGroundPath.UnreachableError);
        float length=0;for(int i=1;i<route.Count;++i)length+=Vector3.Distance(route[i-1],route[i]);
        var prefix=SIMovementRoute.Prefix(route,legLength);
        string failure=WalkingFailure(scene,prefix,pa);
        if(failure!=null)return SIGroundPath.Failed(failure);
        return new(prefix.ToImmutableArray()){Complete=!partial&&reached&&length<=legLength+.01f,Partial=partial};
    }

    // Why a walker could not follow these corners, or null when it can. A too-steep spot is kept in _steepAt.
    string WalkingFailure(Scene scene,Vector3[] prefix,ulong startPoly)
    {
        bool waterStart=Water(scene,startPoly);
        for(int i=1;i<prefix.Length;++i)
            if(!(i==1?SIWalkingSurface.SafeFirstStep(prefix[0],prefix[1]):SIWalkingSurface.SafeStep(prefix[i-1],prefix[i])) && !(i==1&&waterStart))
            {
                _steepAt=Vector3.Lerp(prefix[i-1],prefix[i],.5f);
                return $"Route exceeds player walking slope or step height: {prefix[i-1]} to {prefix[i]}";
            }
        if(SurfaceWalkable(scene,prefix,out string surfaceFailure,out var steep,waterStart))return null;
        _steepAt=steep;
        return surfaceFailure;
    }

    bool SurfaceWalkable(Scene scene,IReadOnlyList<Vector3> path,out string failure,out Vector3? steep,bool waterStart=false)
    {
        failure="Route has no supported floor";steep=null;
        if(path.Count<2)return false;
        bool Sample(Vector3 point,bool allowWater,out Vector3 floor,out bool water)
        {
            ulong poly=0;var nearest=new float[3];floor=default;water=false;
            uint status=scene.Query.findNearestPoly(Mesh(point),[.2f,2,.2f],Filter,ref poly,ref nearest);
            if((Detour.dtStatusFailed(status)||poly==0)&&allowWater)
            {
                // Water mesh lives at the surface. A submerged swimmer may
                // approach it vertically; this never authorizes a ground climb.
                status=scene.Query.findNearestPoly(Mesh(point),[.2f,60,.2f],Filter,ref poly,ref nearest);
                if(Detour.dtStatusFailed(status)||poly==0||!Water(scene,poly))return false;
            }
            if(Detour.dtStatusFailed(status)||poly==0)return false;
            floor=World(nearest);water=Water(scene,poly);
            return Vector2.Distance(new(point.X,point.Y),new(floor.X,floor.Y))<=.25f;
        }
        // A walker standing just off the mesh (beside an NPC behind a counter) steps onto it at the snapped start,
        // the same allowance Snap gives the corridor search.
        int begin=1;
        if(!Sample(path[0],waterStart,out var previous,out bool wasWater))
        {
            if(path.Count<3||Vector2.Distance(new(path[0].X,path[0].Y),new(path[1].X,path[1].Y))>3||
                !Sample(path[1],waterStart,out previous,out wasWater))return false;
            begin=2;
        }
        // The first yards leave the walker's own spot and share its start correction (SafeFirstStep).
        float startAllowance=SIWalkingSurface.StartCorrection,walked=0;var last=path[begin-1];
        // Slope is judged over the last DetailWindow yards: a sustained incline over the walking limit fails, a lone
        // sub-yard bump on an ordinary ramp does not (judging each sample alone stranded bots on the only ramp off
        // a plateau). A single sample may still never be steeper than the mesh's own steep limit.
        var samples=new List<(float Along,Vector3 Floor)>{(0,previous)};int anchor=0;
        for(int i=begin;i<path.Count;++i)
        {
            int count=Math.Max(1,(int)MathF.Ceiling(Vector3.Distance(path[i-1],path[i])));
            for(int step=1;step<=count;++step)
            {
                var point=Vector3.Lerp(path[i-1],path[i],step/(float)count);
                walked+=Vector2.Distance(new(last.X,last.Y),new(point.X,point.Y));last=point;
                if(!Sample(point,wasWater,out var floor,out bool water))
                {failure=$"Route has no supported detail floor at {point}";return false;}
                if(water&&wasWater){samples.Clear();samples.Add((walked,floor));anchor=0;}
                else
                {
                    samples.Add((walked,floor));
                    while(anchor+1<samples.Count-1&&samples[anchor+1].Along<=walked-SIWalkingSurface.DetailWindow)++anchor;
                    float excess=Math.Max(SIWalkingSurface.DetailExcess(samples[anchor].Floor,floor),
                        SIWalkingSurface.DetailExcess(previous,floor,SIWalkingSurface.MeshSteepSlope));
                    if(excess>0&&walked<=SIWalkingSurface.StartCorrectionLength&&excess<=startAllowance){startAllowance-=excess;excess=0;}
                    if(excess>0||Math.Abs(floor.Z-point.Z)>1.5f)
                    {failure=$"Route crosses an unwalkable detail slope: {previous} to {floor} (path {point})";steep=floor;return false;}
                }
                previous=floor;wasWater=water;
            }
        }
        return true;
    }

    // TrinityCore's FindSmoothPath: walk the corridor in short steps on the detail surface, so every
    // point carries real ground height and the line never cuts through a hillside or a wall corner.
    List<Vector3> Smooth(Scene scene,Vector3 start,Vector3 end,ulong[] corridor,int count,Func<bool> cancelled,float legLength,out bool reached)
    {
        reached=false;
        var query=scene.Query;var result=new List<Vector3>();
        // The surface walk can circle on some polygon arrangements (TrinityCore's "most likely a loop").
        // Bound it by the funnel length; the caller then follows the funnel corners instead.
        float funnel=CornerLength(query,start,end,corridor,count);
        float bound=Math.Min(legLength,funnel)*1.6f+40;
        // Room for the polygons a surface move visits ahead of the corridor.
        var polys=new ulong[count+32];Array.Copy(corridor,polys,count);int npolys=count;
        float[] iter=Mesh(start),target=Mesh(end);
        result.Add(start);
        float walked=0;var visited=new ulong[16];
        // Long enough for a full leg plus the corner that ends it.
        while(npolys>0&&result.Count<2048&&walked<legLength+40)
        {
            if(cancelled())throw new OperationCanceledException();
            if(!SteerTarget(query,iter,target,polys,npolys,out var steer,out byte steerFlags,out ulong steerRef))break;
            bool endOfPath=(steerFlags&(byte)Detour.dtStraightPathFlags.DT_STRAIGHTPATH_END)!=0;
            bool offMesh=(steerFlags&(byte)Detour.dtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION)!=0;
            var delta=new float[3];Detour.dtVsub(delta,steer,iter);
            float len=MathF.Sqrt(Detour.dtVdot(delta,delta));
            len=(endOfPath||offMesh)&&len<4?1:4/Math.Max(len,.0001f);
            var moveTarget=new float[3];Detour.dtVmad(moveTarget,iter,delta,len);
            var moved=new float[3];int nvisited=0;
            if(Detour.dtStatusFailed(query.moveAlongSurface(polys[0],iter,moveTarget,Filter,moved,visited,ref nvisited,visited.Length)))break;
            npolys=FixupCorridor(polys,npolys,visited,nvisited);
            if(npolys==0)break;
            float height=moved[1];
            if(!Detour.dtStatusFailed(query.getPolyHeight(polys[0],moved,ref height)))moved[1]=height;
            iter=moved;
            if(endOfPath&&InRange(iter,steer,.3f,1.5f))
            {
                iter=target;
                result.Add(World(iter));
                reached=true;break;
            }
            if(offMesh&&InRange(iter,steer,.3f,1.5f))
            {
                ulong previousRef=0,polyRef=polys[0];int npos=0;
                while(npos<npolys&&polyRef!=steerRef){previousRef=polyRef;polyRef=polys[npos];++npos;}
                for(int i=npos;i<npolys;++i)polys[i-npos]=polys[i];
                npolys-=npos;
                var linkStart=new float[3];var linkEnd=new float[3];
                if(Detour.dtStatusSucceed(scene.Mesh.getOffMeshConnectionPolyEndPoints(previousRef,polyRef,linkStart,linkEnd)))
                {
                    var linkPoint=World(linkStart);walked+=Vector3.Distance(result[^1],linkPoint);result.Add(linkPoint);
                    iter=linkEnd;
                    if(npolys>0&&!Detour.dtStatusFailed(query.getPolyHeight(polys[0],iter,ref height)))iter[1]=height;
                }
            }
            var point=World(iter);walked+=Vector3.Distance(result[^1],point);result.Add(point);
            if(walked>bound&&walked<legLength){reached=false;return null;}
        }
        // Water polygons lie on the surface; the controller lowers swim points into the liquid.
        return result;
    }
    float CornerLength(Detour.dtNavMeshQuery query,Vector3 start,Vector3 end,ulong[] corridor,int count)
    {
        float[] straight=new float[256*3];byte[] flags=new byte[256];ulong[] refs=new ulong[256];int corners=0;
        if(Detour.dtStatusFailed(query.findStraightPath(Mesh(start),Mesh(end),corridor,count,straight,flags,refs,ref corners,256,0))||corners<1)
            return float.MaxValue;
        float length=0;var previous=start;
        for(int i=0;i<corners;++i){var c=new Vector3(straight[i*3+2],straight[i*3],straight[i*3+1]);length+=Vector3.Distance(previous,c);previous=c;}
        return length;
    }
    // Funnel corners with the floor height sampled every few yards between them.
    List<Vector3> Corners(Scene scene,Vector3 start,Vector3 end,ulong[] corridor,int count,float legLength,out bool reached)
    {
        reached=false;var query=scene.Query;
        float[] straight=new float[256*3];byte[] flags=new byte[256];ulong[] refs=new ulong[256];int corners=0;
        var result=new List<Vector3>{start};
        if(Detour.dtStatusFailed(query.findStraightPath(Mesh(start),Mesh(end),corridor,count,straight,flags,refs,ref corners,256,0))||corners<1)return result;
        float walked=0;var previous=start;
        for(int i=0;i<corners;++i)
        {
            var corner=new Vector3(straight[i*3+2],straight[i*3],straight[i*3+1]);
            int steps=Math.Max(1,(int)MathF.Ceiling(Vector2.Distance(new(previous.X,previous.Y),new(corner.X,corner.Y))/4));
            for(int step=1;step<=steps;++step)
            {
                var sample=Vector3.Lerp(previous,corner,step/(float)steps);
                if(step<steps)
                {
                    ulong poly=0;var nearest=new float[3];
                    if(!Detour.dtStatusFailed(query.findNearestPoly(Mesh(sample),[1,6,1],Filter,ref poly,ref nearest))&&poly!=0)sample.Z=nearest[1];
                }
                walked+=Vector3.Distance(result[^1],sample);result.Add(sample);
                if(walked>=legLength+40)return result;
            }
            previous=corner;
        }
        reached=(flags[corners-1]&(byte)Detour.dtStraightPathFlags.DT_STRAIGHTPATH_END)!=0;
        return result;
    }
    static bool InRange(float[] a,float[] b,float r,float h)
    {float dx=b[0]-a[0],dy=b[1]-a[1],dz=b[2]-a[2];return dx*dx+dz*dz<r*r&&Math.Abs(dy)<h;}
    bool SteerTarget(Detour.dtNavMeshQuery query,float[] start,float[] end,ulong[] path,int size,out float[] steer,out byte flags,out ulong reference)
    {
        steer=new float[3];flags=0;reference=0;
        float[] points=new float[3*3];byte[] pointFlags=new byte[3];ulong[] refs=new ulong[3];int n=0;
        uint status=query.findStraightPath(start,end,path,size,points,pointFlags,refs,ref n,3,0);
        if(Detour.dtStatusFailed(status)||n==0)return false;
        int i=0;
        while(i<n)
        {
            if((pointFlags[i]&(byte)Detour.dtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION)!=0||
                !InRange([points[i*3],points[i*3+1],points[i*3+2]],start,.3f,1000))break;
            ++i;
        }
        if(i>=n)return false;
        steer=[points[i*3],start[1],points[i*3+2]];flags=pointFlags[i];reference=refs[i];
        return true;
    }
    static int FixupCorridor(ulong[] path,int npath,ulong[] visited,int nvisited)
    {
        int furthestPath=-1,furthestVisited=-1;
        for(int i=npath-1;i>=0;--i)
        {
            bool found=false;
            for(int j=nvisited-1;j>=0;--j)if(path[i]==visited[j]){furthestPath=i;furthestVisited=j;found=true;}
            if(found)break;
        }
        if(furthestPath==-1||furthestVisited==-1)return npath;
        int req=nvisited-furthestVisited;
        int orig=Math.Min(furthestPath+1,npath);
        if(req>path.Length)req=path.Length;
        int size=Math.Max(0,npath-orig);
        if(req+size>path.Length)size=path.Length-req;
        if(size>0)Array.Copy(path,orig,path,req,size);
        for(int i=0;i<req;++i)path[i]=visited[nvisited-1-i];
        return req+size;
    }
}

// Routes on a worker. Short trips are one navmesh query. Long trips are routed over the map's travel
// graph (a road network of verified walkable links) and walked as a series of navmesh legs toward
// graph waypoints, like turn-by-turn directions.
public sealed partial class SIWorldNavigation
{
    readonly string _data;readonly float _max;
    readonly SINavMeshPlanner _planner;
    public (long Loads,long Evictions,int Tiles,long FileBytes) CacheStatistics=>_planner.CacheStatistics;
    // A trip within this distance is planned directly on the mesh.
    const float DirectRange=220;
    // How far along the graph route each walking leg aims.
    const float WaypointReach=180;
    const float WaypointLeg=1000;
    public SIWorldNavigation(string data,float maximum=600){_data=data;_max=maximum;_planner=new(data);}
    // Offline diagnostics only.
    public static Action<string> Trace;

    SIGroundPath FindGround(SIPathPosition from,SIPathPosition to,Func<bool> cancelled,ImmutableArray<SITravelThreat> threats,SIRouteHints hints)
    {
        _planner.Filter.Use(threats,from.Point);
        try
        {
            if(!from.Valid||!to.Valid||from.MapId!=to.MapId)return SIGroundPath.Failed("Destination is on another map");
            float flat=Vector2.Distance(new(from.Point.X,from.Point.Y),new(to.Point.X,to.Point.Y));
            var graph=SITravelGraphs.Get(_data,from.MapId);
            if(flat>DirectRange&&graph!=null)
            {
                // Keep walking to the waypoint already chosen. Re-deciding the route from every point
                // along a leg made bots turn round between two waypoints.
                if(hints.Via is Vector3 via&&graph.UsableWaypoint(via)&&!SIMovementRoute.ReachedTravelWaypoint(from.Point,via))
                {
                    var onward=_planner.Plan(from,new(from.MapId,via),cancelled,_max,WaypointLeg);
                    if(onward.Success&&!onward.Partial&&Vector3.Distance(onward.Points[^1],from.Point)>3)
                        return RoadWaypoint(onward,via);
                }
                var leg=GraphLeg(graph,from,to,cancelled,hints.Interests.IsDefault?[]:hints.Interests);
                if(leg!=null)return leg;
            }
            if(flat<=_max)
            {
                var direct=_planner.Plan(from,to,cancelled,_max);
                if(direct.Success&&!direct.Partial)return direct;
                // Close on the map but not connected locally (the far side of a gorge, the floor below a
                // ledge): the road network may know the way round.
                if(graph!=null&&flat<=DirectRange&&flat>20&&GraphLeg(graph,from,to,cancelled,hints.Interests.IsDefault?[]:hints.Interests) is SIGroundPath around)return around;
                if(direct.Success||flat<=DirectRange)return direct;
            }
            // No graph route (still building, or disconnected): head straight for the target and let the
            // next leg continue from wherever this one ends.
            var toward=flat>DirectRange?Vector3.Lerp(from.Point,to.Point,DirectRange/flat):to.Point;
            var leg2=_planner.Plan(from,new(to.MapId,toward,false,DirectRange*.5f),cancelled,_max);
            return leg2.Success?leg2 with{Complete=false}:leg2;
        }
        catch(OperationCanceledException){throw;}
        catch(Exception e)when(e is IOException or InvalidDataException or ArgumentException or IndexOutOfRangeException or NullReferenceException)
        {return SIGroundPath.Failed(e.Message);}
    }

    // Terrain cost chooses a sensible route. Useful stops may justify an alternative
    // within the detour allowance, but do not erase the cost of mountain climbing.
    const float InterestReach=60,DetourFactor=1.4f,DetourSlack=150;
    List<int> SmartRoute(SITravelGraph graph,Vector3 from,Vector3 to,HashSet<int> rejected,ImmutableArray<SITravelInterest> interests)
    {
        var shortest=graph.Route(from,to,rejected);
        if(shortest==null||interests.Length==0)return shortest;
        var candidates=new List<List<int>>{shortest};
        var penalty=new Dictionary<int,float>();
        for(int alternative=0;alternative<2;++alternative)
        {
            // Penalise the routes found so far, so the next search finds a genuinely different road.
            foreach(int node in candidates[^1])penalty[node]=penalty.GetValueOrDefault(node,1)*1.8f;
            var other=graph.Route(from,to,rejected,penalty);
            if(other==null)break;
            var nodes=other.ToHashSet();
            if(candidates.All(known=>known.Count(nodes.Contains)<.7f*Math.Max(known.Count,other.Count)))candidates.Add(other);
        }
        float Length(List<int> route)
        {
            float length=Vector3.Distance(from,graph.Position(route[0]))+Vector3.Distance(graph.Position(route[^1]),to);
            for(int i=1;i<route.Count;++i)length+=Vector3.Distance(graph.Position(route[i-1]),graph.Position(route[i]));
            return length;
        }
        float Worth(List<int> route)
        {
            float worth=0;
            foreach(var interest in interests)
            {
                var previous=from;
                foreach(int node in route.Append(-1))
                {
                    var point=node<0?to:graph.Position(node);
                    if(SegmentDistance(interest.Position,previous,point)<=InterestReach){worth+=interest.Worth;break;}
                    previous=point;
                }
            }
            return worth;
        }
        float limit=Length(shortest)*DetourFactor+DetourSlack;
        return candidates.Select(route=>(Route:route,Length:Length(route))).Where(c=>c.Length<=limit)
            .OrderBy(c=>graph.RouteCost(from,to,c.Route)-Worth(c.Route)).First().Route;
    }

    static float SegmentDistance(Vector3 point,Vector3 a,Vector3 b)
    {
        if(Math.Abs(point.Z-a.Z)>40&&Math.Abs(point.Z-b.Z)>40)return float.MaxValue;
        var p=new Vector2(point.X,point.Y);var start=new Vector2(a.X,a.Y);var edge=new Vector2(b.X,b.Y)-start;
        float t=edge.LengthSquared()<.0001f?0:Math.Clamp(Vector2.Dot(p-start,edge)/edge.LengthSquared(),0,1);
        return Vector2.Distance(p,start+edge*t);
    }

    SIGroundPath GraphLeg(SITravelGraph graph,SIPathPosition from,SIPathPosition to,Func<bool> cancelled,ImmutableArray<SITravelInterest> interests)
    {
        var rejected=new HashSet<int>();
        for(int attempt=0;attempt<4;++attempt)
        {
            if(cancelled())throw new OperationCanceledException();
            var route=SmartRoute(graph,from.Point,to.Point,rejected,interests);
            if(route==null||route.Count==0)return null;
            // The route starts at the node nearest the start, which is usually the one just walked past.
            // Never aim back at a node the start is already beyond along the route.
            int first=0;
            while(first+1<route.Count&&SIMovementRoute.Passed(from.Point,graph.Position(route[first]),graph.Position(route[first+1]),
                Vector3.Distance(graph.Position(route[first]),graph.Position(route[first+1]))+30,25))++first;
            // Aim for the furthest waypoint within reach along the route; consecutive waypoints are
            // verified as connected, so the mesh can always walk the stretch between them.
            int chosen=first;float along=Vector3.Distance(from.Point,graph.Position(route[first]));
            for(int i=first+1;i<route.Count;++i)
            {
                along+=Vector3.Distance(graph.Position(route[i-1]),graph.Position(route[i]));
                if(along>WaypointReach)break;
                chosen=i;
            }
            // Close to the destination: the remaining stretch is direct.
            float remaining=Vector2.Distance(new(graph.Position(route[chosen]).X,graph.Position(route[chosen]).Y),new(to.Point.X,to.Point.Y));
            if(chosen==route.Count-1&&remaining<=DirectRange*.6f)
            {
                var direct=_planner.Plan(from,to,cancelled,_max);
                if(direct.Success&&!direct.Partial)return direct;
            }
            // A road instruction is complete within twenty yards. Skip any
            // clustered graph points already inside that radius as well.
            while(SIMovementRoute.ReachedTravelWaypoint(from.Point,graph.Position(route[chosen])))
            {
                if(chosen+1>=route.Count)return null;
                ++chosen;
            }
            var waypoint=graph.Position(route[chosen]);
            // Keep the chosen road direction until its arrival radius, then let
            // the next leg continue without visiting the exact graph coordinate.
            var leg=_planner.Plan(from,new(from.MapId,waypoint),cancelled,_max,WaypointLeg);
            Trace?.Invoke($"    route {route.Count} nodes, start {graph.Position(route[0])}, waypoint {chosen} {waypoint}: {(leg.Success?(leg.Partial?"partial":"ok"):leg.Error)} end {(leg.Success?leg.Points[^1]:default)}");
            if(leg.Success&&!leg.Partial&&Vector3.Distance(leg.Points[^1],from.Point)>3)
                return RoadWaypoint(leg,waypoint);
            // That waypoint is not reachable from here (another floor, a fenced pen): route around it.
            rejected.Add(route[first]);if(chosen!=first)rejected.Add(route[chosen]);
        }
        return null;
    }

    static SIGroundPath RoadWaypoint(SIGroundPath path,Vector3 waypoint) => path with
    {
        Points=SIMovementRoute.ApproachTravelWaypoint(path.Points,waypoint).ToImmutableArray(),
        Complete=false,Waypoint=waypoint
    };
}
