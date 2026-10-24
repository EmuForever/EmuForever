using System;
using System.Collections;
using System.Collections.Generic;

namespace Framework.Collections
{
    /// <summary>Single-owner set. Rejects new entries at capacity; existing entries remain removable.</summary>
    public sealed class BoundedSet<T> : IReadOnlyCollection<T>
    {
        readonly HashSet<T> _items;
        readonly Action<T> _removed;
        public int Capacity { get; }
        public int Count => _items.Count;
        public long RejectedAdds { get; private set; }

        public BoundedSet(int capacity, int initialCapacity = 256, Action<T> onRemoved = null)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            _removed = onRemoved;
            _items = new HashSet<T>(Math.Min(initialCapacity, capacity));
        }

        public bool Add(T item)
        {
            if (Count < Capacity) return _items.Add(item);
            // Never evict a known object: its client still requires a matching destroy delta.
            if (!_items.Contains(item)) ++RejectedAdds;
            return false;
        }
        public bool Contains(T item) => _items.Contains(item);
        public bool Remove(T item)
        {
            if (!_items.Remove(item)) return false;
            _removed?.Invoke(item);
            return true;
        }
        public void Clear()
        {
            if (_removed != null) foreach (var item in _items) _removed(item);
            _items.Clear();
        }
        public HashSet<T>.Enumerator GetEnumerator() => _items.GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
