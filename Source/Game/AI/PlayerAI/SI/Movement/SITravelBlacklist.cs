using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Game.AI.SIMovement;

public sealed record SITravelBlockedPoint(float X, float Y, float Z);

// Coordinates survive regenerated graph node numbers. Only the matching floor
// within two yards is disabled; a tunnel below a bad surface point stays usable.
public sealed class SITravelBlacklist
{
    readonly string _file;
    readonly Vector3[] _nodes;
    readonly object _gate = new();
    SITravelBlockedPoint[] _points;
    volatile FrozenSet<int> _blocked = FrozenSet<int>.Empty;
    int _revision;
    public FrozenSet<int> Nodes => _blocked;
    public int Revision => System.Threading.Volatile.Read(ref _revision);
    public static string FileName(string data, int map) => Path.Combine(data, "navigation", $"si-travel-{map:D4}.blacklist.json");

    public SITravelBlacklist(string data, int map, Vector3[] nodes)
    {
        _file = FileName(data, map); _nodes = nodes;
        _points = File.Exists(_file) ? JsonSerializer.Deserialize<SITravelBlockedPoint[]>(File.ReadAllText(_file))
            ?? throw new InvalidDataException("Empty SI travel blacklist: " + _file) : [];
        _blocked = Resolve(_points);
    }

    FrozenSet<int> Resolve(IEnumerable<SITravelBlockedPoint> points)
    {
        var blocked = new HashSet<int>();
        foreach (var point in points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z))
                throw new InvalidDataException("Nonfinite SI travel blacklist position");
            var position = new Vector3(point.X, point.Y, point.Z);
            int best = -1; float distance = 4.0001f;
            for (int i = 0; i < _nodes.Length; ++i)
            {
                float next = Vector3.DistanceSquared(position, _nodes[i]);
                if (next < distance) { best = i; distance = next; }
            }
            if (best >= 0) blocked.Add(best);
        }
        return blocked.ToFrozenSet();
    }

    public bool Set(int node, bool blocked)
    {
        if ((uint)node >= _nodes.Length) throw new ArgumentOutOfRangeException(nameof(node));
        lock (_gate)
        {
            if (_blocked.Contains(node) == blocked) return false;
            var position = _nodes[node];
            var next = _points.Where(p => Vector3.DistanceSquared(new(p.X, p.Y, p.Z), position) > 4.0001f).ToList();
            if (blocked) next.Add(new(position.X, position.Y, position.Z));
            SITravelEditFile.Save(_file, JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
            _points = next.ToArray();
            _blocked = Resolve(_points);
            System.Threading.Interlocked.Increment(ref _revision);
            return true;
        }
    }
}
