using System;
using System.Collections;
using System.Collections.Generic;

namespace Framework.Collections
{
    /// <summary>Single-owner, unordered, indexed set with O(1) amortized insertion/removal.</summary>
    public sealed class BoundedDenseSet<T> : IReadOnlyList<T> where T : class
    {
        readonly List<T> _items = new();
        // Most typed views in most cells are empty. Allocate their index only on use.
        Dictionary<T, int> _indices;
        public int Capacity { get; }
        public int Count => _items.Count;
        public T this[int index] => _items[index];
        public BoundedDenseSet(int capacity)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }
        public bool Contains(T item) => _indices?.ContainsKey(item) == true;
        public bool Add(T item)
        {
            if (Count == Capacity)
            {
                if (Contains(item)) return false;
                throw new InvalidOperationException("Spatial cell capacity exceeded");
            }
            _indices ??= new(ReferenceEqualityComparer.Instance);
            if (!_indices.TryAdd(item, Count)) return false;
            _items.Add(item);
            return true;
        }
        public bool Remove(T item)
        {
            if (_indices == null || !_indices.Remove(item, out int index)) return false;
            int last = Count - 1;
            if (index != last)
            {
                // Swap removal keeps iteration contiguous without shifting a dense cell.
                T moved = _items[last];
                _items[index] = moved;
                _indices[moved] = index;
            }
            _items.RemoveAt(last);
            return true;
        }
        public List<T>.Enumerator GetEnumerator() => _items.GetEnumerator();
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
