using Game.Entities;
using System.Collections.Concurrent;
using System.Collections.Generic;
namespace Game.AI
{
    public readonly record struct SIQuestReservationKey(int Map, int Instance, bool GameObject, ObjectGuid Spawn);

    // Short leases distribute SI pulls and object use between SI players without reserving quest-giver NPCs.
    public static class SIQuestReservations
    {
        sealed record Lease(ObjectGuid Owner, long Expires);
        static readonly ConcurrentDictionary<SIQuestReservationKey, Lease> Claims = new();

        public static bool Available(SIQuestReservationKey key, ObjectGuid owner, long now) =>
            !Claims.TryGetValue(key, out Lease lease) || lease.Owner == owner || lease.Expires <= now;

        public static bool TryClaim(SIQuestReservationKey key, ObjectGuid owner, long now)
        {
            while (true)
            {
                var next = new Lease(owner, now + 20000);
                if (!Claims.TryGetValue(key, out Lease previous))
                {
                    if (Claims.TryAdd(key, next)) return true;
                    continue;
                }
                if (previous.Owner != owner && previous.Expires > now) return false;
                if (Claims.TryUpdate(key, next, previous)) return true;
            }
        }

        public static void Release(SIQuestReservationKey key, ObjectGuid owner)
        {
            if (Claims.TryGetValue(key, out Lease lease) && lease.Owner == owner)
                Claims.TryRemove(new KeyValuePair<SIQuestReservationKey, Lease>(key, lease));
        }

        public static void Prune(long now)
        {
            foreach (var pair in Claims)
                if (pair.Value.Expires <= now) Claims.TryRemove(pair);
        }
    }
}
