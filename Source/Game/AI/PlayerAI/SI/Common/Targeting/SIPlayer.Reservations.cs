using Game.Entities;
using System;

namespace Game.AI
{
    // Short target leases shared by all bots (no two bots pull the same creature or use the same object),
    // plus each character's stable target preferences.
    public sealed partial class SIPlayer
    {
        SIQuestReservationKey? _reservedQuestTarget;
        long _nextReservationPrune;
        SITargetPreference _targetPreference;
        SITargetPreference TargetPreference => _targetPreference ??= new SITargetPreference(me.GetGUID().GetCounter() > 0 ? (ulong)me.GetGUID().GetCounter() : 1);

        static long SharedNow => Environment.TickCount64;

        SIQuestReservationKey Reservation(ObjectGuid guid, bool gameObject) =>
            new(me.GetMapId(), me.GetInstanceId(), gameObject, guid);

        bool ReserveQuestTarget(ObjectGuid guid, bool gameObject = false)
        {
            SIQuestReservationKey key = Reservation(guid, gameObject);
            if (!SIQuestReservations.TryClaim(key, me.GetGUID(), SharedNow)) return false;
            if (_reservedQuestTarget is { } old && old != key) SIQuestReservations.Release(old, me.GetGUID());
            _reservedQuestTarget = key;
            return true;
        }

        bool ReservationAvailable(ObjectGuid guid, bool gameObject = false) =>
            SIQuestReservations.Available(Reservation(guid, gameObject), me.GetGUID(), SharedNow);

        void ReleaseQuestTarget()
        {
            if (_reservedQuestTarget is { } key) SIQuestReservations.Release(key, me.GetGUID());
            _reservedQuestTarget = null;
        }

        void TickQuestReservation()
        {
            if (_reservedQuestTarget is { } key)
            {
                if (!me.IsAlive() || key.Map != me.GetMapId() || _goal == "pause") ReleaseQuestTarget();
                else SIQuestReservations.TryClaim(key, me.GetGUID(), SharedNow);
            }
            if (SharedNow >= _nextReservationPrune) { _nextReservationPrune = SharedNow + 30000; SIQuestReservations.Prune(SharedNow); }
        }
    }
}
