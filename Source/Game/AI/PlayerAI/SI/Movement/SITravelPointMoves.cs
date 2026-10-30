using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Game.AI.SIMovement;

public sealed record SITravelMovedPoint(float SourceX, float SourceY, float SourceZ, float X, float Y, float Z)
{
    [JsonIgnore] public Vector3 Source => new(SourceX,SourceY,SourceZ);
    [JsonIgnore] public Vector3 Position => new(X,Y,Z);
}

// Operator edits are separate from the generated cache. Flush before atomic
// replacement and retain the previous version; a failed save cannot publish an edit.
public static class SITravelEditFile
{
    public static void Save(string file, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temporary=file+".tmp";
        using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None))
        {
            using(var writer=new StreamWriter(stream,leaveOpen:true)){writer.Write(json);writer.Flush();}
            stream.Flush(true);
        }
        if(File.Exists(file))File.Copy(file,file+".bak",true);
        File.Move(temporary,file,true);
    }
}

public static class SITravelPointMoves
{
    const float LinkRadius=SITravelGraph.Spacing*1.8f;
    public static string FileName(string data,int map)=>Path.Combine(data,"navigation",$"si-travel-{map:D4}.moves.json");

    public static bool Valid(Vector3 point)=>float.IsFinite(point.X)&&float.IsFinite(point.Y)&&float.IsFinite(point.Z)&&
        Math.Abs(point.X)<17066.666f&&Math.Abs(point.Y)<17066.666f;

    public static SITravelMovedPoint[] Read(string data,int map)
    {
        string file=FileName(data,map);
        var moves=File.Exists(file)?JsonSerializer.Deserialize<SITravelMovedPoint[]>(File.ReadAllText(file))??
            throw new InvalidDataException("Empty SI road-point move file: "+file):[];
        if(moves.Any(m=>!Valid(m.Source)||!Valid(m.Position))||moves.Select(m=>m.Source).Distinct().Count()!=moves.Length)
            throw new InvalidDataException("Invalid or duplicate SI road-point move in "+file);
        return moves;
    }

    public static void Save(string data,int map,IEnumerable<SITravelMovedPoint> moves)=>
        SITravelEditFile.Save(FileName(data,map),JsonSerializer.Serialize(moves,new JsonSerializerOptions{WriteIndented=true}));

    static SITravelGraph Reconnect(SITravelGraph basis,Vector3[] anchors,Vector3[] positions,HashSet<int> changed,
        Func<Vector3,Vector3,float> connect)
    {
        var edges=basis.Links().Where(e=>!changed.Contains(e.A)&&!changed.Contains(e.B)).ToList();
        var checkedPairs=new HashSet<(int,int)>();
        foreach(int node in changed)
        {
            var from=positions[node];
            var nearby=Enumerable.Range(0,positions.Length).Where(n=>n!=node&&Vector3.DistanceSquared(from,positions[n])<=LinkRadius*LinkRadius)
                .OrderBy(n=>Vector3.DistanceSquared(from,positions[n])).ThenBy(n=>n).Take(32);
            foreach(int other in nearby)
            {
                int a=Math.Min(node,other),b=Math.Max(node,other);
                if(!checkedPairs.Add((a,b)))continue;
                float cost=connect(positions[a],positions[b]);
                if(float.IsFinite(cost)&&cost>0)edges.Add((a,b,cost));
            }
        }
        return basis.Edited(anchors,positions,edges);
    }

    // Reapply edits to the generated coordinates, matching by original position.
    // If regeneration omits an edited source, retain it as an explicit manual node.
    public static SITravelGraph Load(string data,SITravelGraph generated,Func<Vector3,Vector3,float> connect)
    {
        var moves=Read(data,generated.Map);
        if(moves.Length==0)return generated;
        var anchors=Enumerable.Range(0,generated.NodeCount).Select(generated.SourcePosition).ToList();
        var positions=Enumerable.Range(0,generated.NodeCount).Select(generated.Position).ToList();
        var changed=new HashSet<int>();
        foreach(var move in moves)
        {
            int match=-1;float distance=4.0001f;
            for(int i=0;i<generated.NodeCount;++i)
            {
                float next=Vector3.DistanceSquared(anchors[i],move.Source);
                if(next<distance&&!changed.Contains(i)){match=i;distance=next;}
            }
            if(match<0){match=anchors.Count;anchors.Add(move.Source);positions.Add(move.Position);}
            else {anchors[match]=move.Source;positions[match]=move.Position;}
            changed.Add(match);
        }
        return Reconnect(generated,anchors.ToArray(),positions.ToArray(),changed,connect);
    }

    public static SITravelGraph Move(string data,SITravelGraph graph,int node,Vector3 position,Func<Vector3,Vector3,float> connect)
    {
        if((uint)node>=graph.NodeCount)throw new ArgumentOutOfRangeException(nameof(node));
        if(!Valid(position))throw new ArgumentException("Your position is outside valid map coordinates.");
        if(graph.Position(node)==position)return graph;
        var anchors=Enumerable.Range(0,graph.NodeCount).Select(graph.SourcePosition).ToArray();
        var positions=Enumerable.Range(0,graph.NodeCount).Select(graph.Position).ToArray();
        positions[node]=position;
        var next=Reconnect(graph,anchors,positions,new(){node},connect);
        if(next.Degree(node)==0)throw new InvalidOperationException("No walkable road connection near your position. Stand on reachable ground near other road points; the point was not moved.");
        var source=anchors[node];
        var moves=Read(data,graph.Map).Where(m=>m.Source!=source).ToList();
        moves.Add(new(source.X,source.Y,source.Z,position.X,position.Y,position.Z));
        next.ContinueEditsFrom(graph,data);
        Save(data,graph.Map,moves);
        return next;
    }
}
