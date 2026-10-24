using Game.Entities;
using System.Collections.Generic;
using System.Threading.Channels;

namespace Game.Maps
{
    // A bounded per-thread cache, not a work queue. When full a returned scratch object
    // is discarded; gameplay notifications are never dropped or blocked.
    internal sealed class VisibilityScratch
    {
        // One pool per thread: a shared channel made every visibility update on both map threads contend
        // for the same lock. A notifier is rented and returned on its own map thread.
        [System.ThreadStatic] static Stack<VisibilityScratch> _pool;
        static Stack<VisibilityScratch> Pool => _pool ??= new();

        internal readonly HashSet<ObjectGuid> Remaining = new(256);
        internal readonly List<WorldObject> Created = new(256);
        internal static VisibilityScratch Rent() => Pool.TryPop(out var item) ? item : new();
        internal void Return()
        {
            Remaining.Clear();
            Created.Clear(); // Do not retain world objects between maps or unloads.
            if (Pool.Count < 64) Pool.Push(this);
        }
    }
}
