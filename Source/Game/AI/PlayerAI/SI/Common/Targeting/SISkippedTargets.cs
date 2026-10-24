using Game.Entities;
using System.Collections.Generic;
namespace Game.AI;
// No timed retry loop on a known failed corpse/optional creature. Bounded FIFO
// memory prevents a long-lived bot accumulating every GUID it has ever seen.
public sealed class SISkippedTargets
{
    readonly HashSet<ObjectGuid> _set=new();readonly Queue<ObjectGuid> _order=new();
    readonly int _capacity;
    public SISkippedTargets(int capacity=4096){if(capacity<1)throw new System.ArgumentOutOfRangeException(nameof(capacity));_capacity=capacity;}
    public bool Contains(ObjectGuid guid)=>_set.Contains(guid);
    public void Add(ObjectGuid guid)
    {if(_set.Contains(guid))return;while(_set.Count>=_capacity)_set.Remove(_order.Dequeue());_set.Add(guid);_order.Enqueue(guid);}
}
