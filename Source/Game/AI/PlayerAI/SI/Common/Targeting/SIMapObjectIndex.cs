using Game.Entities;
using Game.Maps;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Game.AI
{
    // One spatial snapshot of a map's creatures and gameobjects, shared by every SI player on it and
    // rebuilt at most once a second. Hundreds of bots each visiting every grid cell within their search
    // radius every second was the largest single cost of a map update.
    // Map-thread only: a Map instance is always updated by one thread at a time.
    sealed class SIMapObjectIndex
    {
        const float Cell = 48;
        const long RebuildMilliseconds = 1000;
        // Objects move after a rebuild; widen the cell query so a recently moved one is still found.
        const float Drift = 16;
        static readonly ConditionalWeakTable<Map, SIMapObjectIndex> Indexes = new();
        readonly Dictionary<long, List<WorldObject>> _cells = new();
        readonly List<Position> _people = new();
        // Not long.MinValue: "now - _builtAt" would overflow negative and the index would never build.
        long _builtAt = -RebuildMilliseconds;
        bool _built;

        public static SIMapObjectIndex For(Map map) => Indexes.GetValue(map, _ => new SIMapObjectIndex());

        static long Key(int x, int y) => ((long)x << 32) | (uint)y;
        static int Coordinate(float value) => (int)MathF.Floor(value / Cell);

        void Refresh(Map map)
        {
            long now = Environment.TickCount64;
            if (_built && now - _builtAt < RebuildMilliseconds) return;
            _builtAt = now; _built = true;
            // Retain the lists between rebuilds; drop the empty ones so the table follows the loaded grids.
            List<long> empty = null;
            foreach (var (key, list) in _cells)
            {
                if (list.Count == 0) (empty ??= new()).Add(key);
                list.Clear();
            }
            if (empty != null) foreach (long key in empty) _cells.Remove(key);
            _people.Clear();
            foreach (Player player in map.GetPlayers())
                if (player.IsInWorld && player.GetSession()?.IsServerControlled == false) _people.Add(new Position(player.GetPosition()));
            foreach (WorldObject obj in map.GetObjectsStore().Values)
            {
                if (obj is not (Creature or GameObject) || !obj.IsInWorld) continue;
                long key = Key(Coordinate(obj.GetPositionX()), Coordinate(obj.GetPositionY()));
                if (!_cells.TryGetValue(key, out var list)) _cells.Add(key, list = new());
                list.Add(obj);
            }
        }

        // Whether a real (non-bot) player was within range at the last rebuild.
        public bool Observed(WorldObject center, float range)
        {
            Refresh(center.GetMap());
            foreach (Position person in _people)
                if (center.GetExactDist2dSq(person) <= range * range) return true;
            return false;
        }

        // Same result as the grid searchers: in world, same phase, within range in 2D.
        public void Collect(WorldObject center, float range, List<Creature> creatures, List<GameObject> objects)
        {
            Map map = center.GetMap();
            Refresh(map);
            float x = center.GetPositionX(), y = center.GetPositionY();
            int minX = Coordinate(x - range - Drift), maxX = Coordinate(x + range + Drift);
            int minY = Coordinate(y - range - Drift), maxY = Coordinate(y + range + Drift);
            float rangeSquared = range * range;
            for (int cx = minX; cx <= maxX; ++cx)
                for (int cy = minY; cy <= maxY; ++cy)
                {
                    if (!_cells.TryGetValue(Key(cx, cy), out var list)) continue;
                    foreach (WorldObject obj in list)
                    {
                        if (!obj.IsInWorld || obj.GetMap() != map) continue;
                        float dx = obj.GetPositionX() - x, dy = obj.GetPositionY() - y;
                        // The object's view of the searcher, as the grid searchers check it; CanSee is not
                        // symmetric for personal phases.
                        if (dx * dx + dy * dy > rangeSquared || !obj.InSamePhase(center)) continue;
                        if (obj is Creature creature) creatures.Add(creature);
                        else objects.Add((GameObject)obj);
                    }
                }
        }
    }
}
