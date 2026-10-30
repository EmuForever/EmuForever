using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Game.AI.SIMovement;

// A continent's road network for long trips, in the spirit of the travel nodes used by other bot cores.
// Nodes are walkable floor points sampled on a regular grid (several per column, so caves, tunnels and
// cities under the terrain get their own); edges are navmesh paths between neighbouring nodes that were
// verified walkable when the graph was built. Routing is A* over the edges, then the bot walks navmesh
// legs from waypoint to waypoint. Immutable once built: workers share it without locks.
public sealed class SITravelGraph
{
    public const int Version=1;
    public const float Spacing=533.3333f/4; // four columns per mmap tile
    public readonly int Map;
    readonly Vector3[] _nodes;
    readonly int[] _edgeStart,_edgeTo;
    readonly float[] _edgeCost;
    readonly Dictionary<long,int[]> _cells;
    Vector3[] _anchors;
    SITravelGraph _generated;
    int _editRevision;
    SITravelBlacklist _blacklist;
    public int Revision => _editRevision+(_blacklist?.Revision ?? 0);
    public bool IsBlacklisted(int node) => _blacklist?.Nodes.Contains(node) == true;
    public void LoadBlacklist(string data) => _blacklist = new(data, Map, _anchors);
    public bool SetBlacklisted(int node, bool value) => (_blacklist ?? throw new InvalidOperationException("Travel blacklist is not loaded")).Set(node, value);
    public IEnumerable<int> NearbyNodes(Vector3 point,float radius,int limit=100) => Enumerable.Range(0,NodeCount)
        .Where(n=>Vector3.DistanceSquared(point,_nodes[n])<=radius*radius)
        .OrderBy(n=>Vector3.DistanceSquared(point,_nodes[n])).ThenBy(n=>n).Take(limit);
    public IEnumerable<int> BlacklistedNodes => _blacklist == null ? Enumerable.Empty<int>() : _blacklist.Nodes.OrderBy(n=>n);
    public bool BlacklistedWaypoint(Vector3 point) => _blacklist?.Nodes.Any(n=>Vector3.DistanceSquared(point,_nodes[n])<=4) == true;
    public int NodeCount=>_nodes.Length;
    public int EdgeCount=>_edgeTo.Length/2;
    public Vector3 Position(int node)=>_nodes[node];
    public Vector3 SourcePosition(int node)=>_anchors[node];
    public bool IsMoved(int node)=>_nodes[node]!=_anchors[node];
    public bool UsableWaypoint(Vector3 point)=>Near(point,2,8).Any(n=>Vector3.DistanceSquared(point,_nodes[n])<.0001f);
    public int Degree(int node)=>_edgeStart[node+1]-_edgeStart[node];
    // Prefer a sensible valley/road detour over climbing and descending a
    // mountain. A graph marker is a floor sample, not permission to climb it.
    public static float TerrainCost(Vector3 from,Vector3 to,float length)=>Math.Max(length,Vector3.Distance(from,to))+6*Math.Abs(to.Z-from.Z);
    public float RouteCost(Vector3 start,Vector3 goal,IReadOnlyList<int> route)
    {
        if(route.Count==0)return float.PositiveInfinity;
        float cost=TerrainCost(start,_nodes[route[0]],Vector3.Distance(start,_nodes[route[0]]))*2.5f;
        for(int i=1;i<route.Count;++i)
        {
            int a=route[i-1],b=route[i];
            int edge=Array.IndexOf(_edgeTo,b,_edgeStart[a],_edgeStart[a+1]-_edgeStart[a]);
            if(edge<0)return float.PositiveInfinity;
            cost+=TerrainCost(_nodes[a],_nodes[b],_edgeCost[edge]);
        }
        return cost+TerrainCost(_nodes[route[^1]],goal,Vector3.Distance(_nodes[route[^1]],goal));
    }
    internal void ContinueEditsFrom(SITravelGraph previous,string data)
    {
        // Live snapshots keep the same node identities. Share blacklist state so
        // a ban arriving while geometry is rebuilt cannot be lost at publication.
        _blacklist=previous._blacklist??new(data,Map,_anchors);
        _editRevision=previous._editRevision+1;
    }
    internal SITravelGraph Edited(Vector3[] anchors,Vector3[] positions,List<(int A,int B,float Cost)> edges)=>
        new(Map,positions,edges){_anchors=anchors,_generated=_generated};
    internal IEnumerable<(int A,int B,float Cost)> Links()
    {
        for(int a=0;a<_nodes.Length;++a)for(int e=_edgeStart[a];e<_edgeStart[a+1];++e)
            if(_edgeTo[e]>a)yield return(a,_edgeTo[e],_edgeCost[e]);
    }
    // Connected component of every node, for offline tests.
    public int[] Components()
    {
        var component=Enumerable.Repeat(-1,_nodes.Length).ToArray();int next=0;var stack=new Stack<int>();
        for(int i=0;i<_nodes.Length;++i)
        {
            if(component[i]>=0)continue;
            component[i]=next;stack.Push(i);
            while(stack.TryPop(out int n))
                for(int e=_edgeStart[n];e<_edgeStart[n+1];++e)
                    if(component[_edgeTo[e]]<0){component[_edgeTo[e]]=next;stack.Push(_edgeTo[e]);}
            ++next;
        }
        return component;
    }

    public SITravelGraph(int map,Vector3[] nodes,List<(int A,int B,float Cost)> edges)
    {
        Map=map;_nodes=nodes;_anchors=nodes;_generated=this;
        var degree=new int[nodes.Length+1];
        foreach(var (a,b,_) in edges){++degree[a];++degree[b];}
        _edgeStart=new int[nodes.Length+1];
        for(int i=0;i<nodes.Length;++i)_edgeStart[i+1]=_edgeStart[i]+degree[i];
        _edgeTo=new int[_edgeStart[^1]];_edgeCost=new float[_edgeStart[^1]];
        var fill=(int[])_edgeStart.Clone();
        foreach(var (a,b,cost) in edges)
        {
            _edgeTo[fill[a]]=b;_edgeCost[fill[a]++]=cost;
            _edgeTo[fill[b]]=a;_edgeCost[fill[b]++]=cost;
        }
        _cells=Enumerable.Range(0,nodes.Length).Where(i=>_edgeStart[i+1]>_edgeStart[i])
            .GroupBy(i=>Key(Cell(nodes[i].X),Cell(nodes[i].Y))).ToDictionary(g=>g.Key,g=>g.ToArray());
    }
    internal static int Cell(float value)=>(int)MathF.Floor(value/Spacing);
    internal static long Key(int x,int y)=>((long)x<<32)|(uint)y;

    // Connected nodes near a point, nearest first. Height differences count triple so that the floor
    // the point is actually on wins over a tunnel or a roof directly above or below it.
    List<int> Near(Vector3 point,float radius,int limit)
    {
        var found=new List<(int Node,float Score)>();
        int reach=(int)MathF.Ceiling(radius/Spacing);
        int cx=Cell(point.X),cy=Cell(point.Y);
        for(int x=cx-reach;x<=cx+reach;++x)for(int y=cy-reach;y<=cy+reach;++y)
        {
            if(!_cells.TryGetValue(Key(x,y),out var nodes))continue;
            foreach(int node in nodes)
            {
                if(IsBlacklisted(node))continue;
                var p=_nodes[node];float flat=Vector2.Distance(new(p.X,p.Y),new(point.X,point.Y));
                if(flat>radius)continue;
                found.Add((node,flat+3*Math.Abs(p.Z-point.Z)));
            }
        }
        return found.OrderBy(f=>f.Score).Take(limit).Select(f=>f.Node).ToList();
    }

    // Candidate entry/exit nodes for a point: only the ones it is standing next to when there are any,
    // otherwise the nearest few.
    List<int> Endpoints(Vector3 point,IReadOnlySet<int> rejected)
    {
        var near=Near(point,Spacing*1.6f,8).Where(n=>!rejected.Contains(n)).ToList();
        var touching=near.Where(n=>Vector3.Distance(_nodes[n],point)<=12).ToList();
        if(touching.Count>0)return touching;
        // A node on the clifftop above a valley point is close on the map and useless as a way in.
        var level=near.Where(n=>Math.Abs(_nodes[n].Z-point.Z)<=25).ToList();
        return level.Count>0?level:near;
    }

    // The closest connected node a stranded point could step to directly, if any.
    public int? Hop(Vector3 point,float horizontal,float vertical)
    {
        int? best=null;float bestDistance=float.MaxValue;
        foreach(int node in Near(point,horizontal,8))
        {
            var p=_nodes[node];if(Math.Abs(p.Z-point.Z)>vertical)continue;
            float distance=Vector3.Distance(p,point);
            if(distance<bestDistance){bestDistance=distance;best=node;}
        }
        return best;
    }

    // Waypoints from near the start to near the goal, or null when they are not connected.
    // Penalty multiplies the cost of entering a node, for finding alternative routes.
    public List<int> Route(Vector3 start,Vector3 goal,IReadOnlySet<int> rejected,IReadOnlyDictionary<int,float> penalty=null)
    {
        if(_blacklist?.Nodes.Count>0)rejected=rejected.Concat(_blacklist.Nodes).ToHashSet();
        var sources=Endpoints(start,rejected);
        var targets=Endpoints(goal,rejected).ToHashSet();
        if(sources.Count==0||targets.Count==0)return null;
        var cost=new Dictionary<int,float>();var parent=new Dictionary<int,int>();
        var open=new PriorityQueue<int,float>();
        float Heuristic(int node)=>Vector3.Distance(_nodes[node],goal);
        foreach(int source in sources)
        {
            // Reaching a source is not verified; a far one is a guess, so it has to earn its place.
            float g=TerrainCost(start,_nodes[source],Vector3.Distance(start,_nodes[source]))*2.5f;
            cost[source]=g;parent[source]=-1;open.Enqueue(source,g+Heuristic(source));
        }
        var closed=new HashSet<int>();
        int bestTarget=-1;float bestCost=float.PositiveInfinity;
        while(open.TryDequeue(out int node,out float estimate))
        {
            if(estimate>=bestCost)break;
            if(!closed.Add(node))continue;
            if(targets.Contains(node))
            {
                float total=cost[node]+TerrainCost(_nodes[node],goal,Vector3.Distance(_nodes[node],goal));
                if(total<bestCost){bestCost=total;bestTarget=node;}
            }
            if(closed.Count>200000)return null;
            float g=cost[node];
            for(int e=_edgeStart[node];e<_edgeStart[node+1];++e)
            {
                int next=_edgeTo[e];
                if(closed.Contains(next)||rejected.Contains(next))continue;
                float candidate=g+TerrainCost(_nodes[node],_nodes[next],_edgeCost[e])*(penalty!=null&&penalty.TryGetValue(next,out float factor)?factor:1);
                if(cost.TryGetValue(next,out float known)&&known<=candidate)continue;
                cost[next]=candidate;parent[next]=node;open.Enqueue(next,candidate+Heuristic(next));
            }
        }
        if(bestTarget<0)return null;
        var path=new List<int>();
        for(int at=bestTarget;at!=-1;at=parent[at])path.Add(at);
        path.Reverse();return path;
    }

    public void Save(string file,string signature)
    {
        if(_generated!=this){_generated.Save(file,signature);return;}
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        string temporary=file+".tmp";
        using(var writer=new BinaryWriter(File.Create(temporary)))
        {
            writer.Write("SITG");writer.Write(Version);writer.Write(signature);writer.Write(Map);
            writer.Write(_nodes.Length);
            foreach(var node in _nodes){writer.Write(node.X);writer.Write(node.Y);writer.Write(node.Z);}
            writer.Write(EdgeCount);
            for(int a=0;a<_nodes.Length;++a)for(int e=_edgeStart[a];e<_edgeStart[a+1];++e)
                if(_edgeTo[e]>a){writer.Write(a);writer.Write(_edgeTo[e]);writer.Write(_edgeCost[e]);}
        }
        File.Move(temporary,file,true);
    }
    public static SITravelGraph Load(string file,string signature)
    {
        if(!File.Exists(file))return null;
        try
        {
            using var reader=new BinaryReader(File.OpenRead(file));
            if(reader.ReadString()!="SITG"||reader.ReadInt32()!=Version||reader.ReadString()!=signature)return null;
            int map=reader.ReadInt32();
            var nodes=new Vector3[reader.ReadInt32()];
            for(int i=0;i<nodes.Length;++i)nodes[i]=new(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            int count=reader.ReadInt32();var edges=new List<(int,int,float)>(count);
            for(int i=0;i<count;++i)edges.Add((reader.ReadInt32(),reader.ReadInt32(),reader.ReadSingle()));
            return new SITravelGraph(map,nodes,edges);
        }
        catch(Exception e)when(e is IOException or EndOfStreamException or IndexOutOfRangeException){return null;}
    }
}

// Graphs are loaded from Data/navigation, or built in the background on first use and cached there.
// Until a map's graph is ready its bots route leg by leg on the mesh alone.
public static class SITravelGraphs
{
    // Small instances never need one; open-world maps do.
    const int MinimumTiles=100;
    static readonly ConcurrentDictionary<int,SITravelGraph> Ready=new();
    static readonly ConcurrentDictionary<int,byte> Started=new();
    static readonly ConcurrentDictionary<int,object> EditGates=new();
    static readonly SemaphoreSlim BuildSlot=new(1,1);
    public static string Statistics=>Ready.IsEmpty?"SI travel graph: none loaded":
        "SI travel graph: "+string.Join(", ",Ready.Values.OrderBy(g=>g.Map).Select(g=>$"map {g.Map} {g.NodeCount} nodes/{g.EdgeCount} links"))+
        (Started.Count>Ready.Count?$"; {Started.Count-Ready.Count} building":"");

    // SI_TRAVEL_GRAPH=0 disables graph routing (offline comparisons of the mesh planner alone).
    static readonly bool Disabled=Environment.GetEnvironmentVariable("SI_TRAVEL_GRAPH")=="0";
    public static SITravelGraph Get(string data,int map)
    {
        if(Disabled)return null;
        if(Ready.TryGetValue(map,out var graph))return graph;
        if(Started.TryAdd(map,0))
        {
            var thread=new Thread(()=>LoadOrBuild(data,map)){IsBackground=true,Priority=ThreadPriority.BelowNormal,Name=$"SI travel graph {map}"};
            thread.Start();
        }
        return null;
    }

    // Blocking load or build, for offline tools.
    public static SITravelGraph Ensure(string data,int map)
    {
        if(Ready.TryGetValue(map,out var graph))return graph;
        Started.TryAdd(map,0);
        LoadOrBuild(data,map);
        return Ready.GetValueOrDefault(map);
    }

    static string[] Tiles(string data,int map)=>Directory.Exists(Path.Combine(data,"mmaps"))?
        Directory.GetFiles(Path.Combine(data,"mmaps"),$"{map:D4}_*.mmtile"):[];
    static string Signature(string[] tiles)=>
        $"{tiles.Length}:{tiles.Sum(t=>new FileInfo(t).Length)}:{tiles.Select(t=>File.GetLastWriteTimeUtc(t).Ticks).DefaultIfEmpty().Max()}";
    public static string GraphFile(string data,int map)=>Path.Combine(data,"navigation",$"si-travel-{map:D4}.bin");
    public static int Revision(int map) => Ready.TryGetValue(map,out var graph) ? graph.Revision : 0;

    static Func<Vector3,Vector3,float> Connections(string data,int map)
    {
        var planner=new SINavMeshPlanner(data);
        return (a,b)=>
        {
            float length=planner.Connect(map,a,b,Vector3.Distance(a,b)*2.5f+20,out bool water);
            return length*(water?1.6f:1);
        };
    }

    public static SITravelGraph Move(string data,SITravelGraph graph,int node,Vector3 position)
    {
        lock(EditGates.GetOrAdd(graph.Map,_=>new object()))
        {
            if(Ready.TryGetValue(graph.Map,out var current)&&current!=graph)
                throw new InvalidOperationException("This road graph changed during the command; retry with its current point.");
            var next=SITravelPointMoves.Move(data,graph,node,position,Connections(data,graph.Map));
            Ready[graph.Map]=next;
            return next;
        }
    }

    static void LoadOrBuild(string data,int map)
    {
        try
        {
            var tiles=Tiles(data,map);
            if(tiles.Length<MinimumTiles||!SINavMeshPlanner.HasMesh(data,map))return;
            string signature=Signature(tiles),file=GraphFile(data,map);
            var graph=SITravelGraph.Load(file,signature);
            if(graph==null)
            {
                BuildSlot.Wait();
                try
                {
                    Log.outInfo(LogFilter.SI,$"SI travel graph: building map {map} from {tiles.Length} mmap tiles (one-time, cached to {file}).");
                    var watch=System.Diagnostics.Stopwatch.StartNew();
                    graph=Build(data,map,tiles);
                    graph.Save(file,signature);
                    Log.outInfo(LogFilter.SI,$"SI travel graph: map {map} built in {watch.Elapsed.TotalSeconds:F0} s, {graph.NodeCount} nodes, {graph.EdgeCount} links.");
                }
                finally{BuildSlot.Release();}
            }
            graph=SITravelPointMoves.Load(data,graph,Connections(data,map));
            graph.LoadBlacklist(data);
            Ready[map]=graph;
        }
        catch(Exception e){Log.outError(LogFilter.SI,$"SI travel graph: map {map} unavailable: {e.Message}");}
    }

    static (int X,int Y)? TileOf(string file)
    {
        var parts=Path.GetFileNameWithoutExtension(file).Split('_');
        return parts.Length==3&&int.TryParse(parts[1],out int x)&&int.TryParse(parts[2],out int y)?(x,y):null;
    }

    public static SITravelGraph Build(string data,int map,string[] tileFiles)
    {
        var tiles=tileFiles.Select(TileOf).Where(t=>t!=null).Select(t=>t.Value).OrderBy(t=>t.X).ThenBy(t=>t.Y).ToArray();
        int workers=Math.Clamp(Environment.ProcessorCount/4,1,3);
        // Sampling: grid columns per tile, each with every distinct floor level found in it.
        var columns=new ConcurrentBag<(long Cell,Vector3[] Levels)>();
        Parallel.ForEach(Partitioner.Create(0,tiles.Length,Math.Max(1,tiles.Length/(workers*8))),new ParallelOptions{MaxDegreeOfParallelism=workers},
            ()=>new SINavMeshPlanner(data),(range,_,planner)=>
        {
            for(int t=range.Item1;t<range.Item2;++t)
            {
                var (tx,ty)=tiles[t];
                // Tile (tx,ty) spans world X down from (32-tx)*size and Y down from (32-ty)*size.
                float top=(32-tx)*533.3333f,left=(32-ty)*533.3333f;
                for(int i=0;i<4;++i)for(int j=0;j<4;++j)
                {
                    var center=new Vector3(top-(i+.5f)*SITravelGraph.Spacing,left-(j+.5f)*SITravelGraph.Spacing,0);
                    var levels=planner.SurfaceLevels(map,tx,ty,center,SITravelGraph.Spacing*.35f);
                    if(levels.Count>0)columns.Add((CellKey(center),levels.ToArray()));
                }
            }
            return planner;
        },_=>{});
        Log.outInfo(LogFilter.SI,$"SI travel graph: map {map} sampled {columns.Count} columns");
        var nodes=new List<Vector3>();var byCell=new Dictionary<long,List<int>>();
        foreach(var (cell,levels) in columns.OrderBy(c=>c.Cell))
        {
            var list=byCell.TryGetValue(cell,out var existing)?existing:byCell[cell]=new();
            foreach(var level in levels){list.Add(nodes.Count);nodes.Add(level);}
        }
        // Links: each node to the nodes of its eight neighbouring columns, kept when the mesh connects
        // them without a long detour. Ordered by tile so each worker's tile cache stays warm.
        var order=Enumerable.Range(0,nodes.Count).OrderBy(n=>SINavMeshPlanner.Tile(nodes[n]).X).ThenBy(n=>SINavMeshPlanner.Tile(nodes[n]).Y).ToArray();
        var edges=new ConcurrentBag<(int,int,float)>();
        int done=0;
        Parallel.ForEach(Partitioner.Create(0,order.Length,Math.Max(1,order.Length/(workers*16))),new ParallelOptions{MaxDegreeOfParallelism=workers},
            ()=>new SINavMeshPlanner(data),(range,_,planner)=>
        {
            for(int k=range.Item1;k<range.Item2;++k)
            {
                int a=order[k];var p=nodes[a];int cx=Cell(p.X),cy=Cell(p.Y);
                for(int dx=-1;dx<=1;++dx)for(int dy=-1;dy<=1;++dy)
                {
                    if(dx==0&&dy==0||!byCell.TryGetValue(Key(cx+dx,cy+dy),out var neighbours))continue;
                    foreach(int b in neighbours)
                    {
                        if(b<=a)continue;
                        var q=nodes[b];float straight=Vector3.Distance(p,q);
                        if(Math.Abs(p.Z-q.Z)>straight*1.2f+10)continue; // a cliff face, not a slope
                        float length;bool water;
                        try{length=planner.Connect(map,p,q,straight*2.5f+20,out water);}
                        catch(InvalidDataException){continue;}
                        if(length>0)edges.Add((a,b,length*(water?1.6f:1)));
                    }
                }
                int finished=Interlocked.Increment(ref done);
                if(finished%(Math.Max(1,order.Length/10))==0)Log.outInfo(LogFilter.SI,$"SI travel graph: map {map} links {finished*100/Math.Max(1,order.Length)}%");
            }
            return planner;
        },_=>{});
        return new SITravelGraph(map,nodes.ToArray(),edges.ToList());
    }
    static int Cell(float value)=>SITravelGraph.Cell(value);
    static long Key(int x,int y)=>SITravelGraph.Key(x,y);
    static long CellKey(Vector3 p)=>Key(Cell(p.X),Cell(p.Y));
}
