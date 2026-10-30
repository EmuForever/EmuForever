using Framework.Constants;
using Game.Entities;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        readonly Dictionary<ObjectGuid, long> _looted = new();
        readonly SISkippedTargets _failedLoot=new();
        readonly SILootAttempt _lootAttempt = new();
        ObjectGuid _lootTarget;
        long _nextLoot;
        long _lootAnimationUntil, _lootMoneyBefore;
        int _lootItemsBefore;

        void CloseLootAnimation()
        {
            if (_lootAnimationUntil == 0 && me.GetLootGUID().IsEmpty() && !me.HasUnitFlag(UnitFlags.Looting) && me.GetAELootView().Count == 0) return;
            _lootAnimationUntil = 0;
            me.GetSession().CloseLootForSI();
        }

        bool TickLootAnimation()
        {
            if (_lootAnimationUntil == 0) return false;
            if (!me.IsAlive() || me.IsInCombat() || me.IsBeingTeleported() || _goal == "pause")
            { CloseLootAnimation(); return false; }
            if (Now < _lootAnimationUntil) { Behavior = "Loot"; _task?.Suspend(Now); return true; }
            try { me.GetSession().LootOpenViewForSI(); }
            finally { CloseLootAnimation(); }
            if (ItemsLooted > _lootItemsBefore || me.GetMoney() > _lootMoneyBefore)
            { NoteGameplayProgress(); EquipUpgrades(); }
            else SkipUnlootableCorpse(_lootTarget);
            return false;
        }

        void SkipUnlootableCorpse(ObjectGuid corpse)
        {
            _failedLoot.Add(corpse);
            Report("loot_failed", "Core could not transfer the remaining loot; skipping this corpse.");
        }

        SIBehaviorResult LootCorpse(Creature corpse)
        {
            if (corpse == null || corpse.IsAlive() || !me.IsAllowedToLoot(corpse)) return SIBehaviorResult.Success;
            // A corpse must not retain the encounter while the player needs to
            // respond to combat. It can be considered again after combat ends.
            if (me.IsInCombat()) { CloseLootAnimation(); return SIBehaviorResult.Success; }
            if (_failedLoot.Contains(corpse.GetGUID()) || _looted.GetValueOrDefault(corpse.GetGUID()) > Now) return SIBehaviorResult.Success;
            if (_lootAttempt.Expired(corpse.GetGUID(), Now))
            {
                CloseLootAnimation(); CancelTravel(); _failedLoot.Add(corpse.GetGUID());
                Report("loot_failed", "Corpse approach/loot exceeded twenty seconds; skipping this corpse.");
                return SIBehaviorResult.Failure;
            }
            if (_lootTarget != corpse.GetGUID()) { _lootTarget = corpse.GetGUID(); _nextLoot = 0; }
            SIBehaviorResult approach = MoveWithin(corpse, SharedConst.InteractionDistance - 1.5f, "Looting " + corpse.GetName());
            if (approach == SIBehaviorResult.Failure) { _failedLoot.Add(corpse.GetGUID()); return approach; }
            if (approach != SIBehaviorResult.Success || Now < _nextLoot) return SIBehaviorResult.Running;
            _nextLoot = Now + 750;
            _lootItemsBefore = ItemsLooted;
            _lootMoneyBefore = me.GetMoney();
            CancelTravel();
            me.GetSession().LootForSI(corpse);
            if (!me.GetLootGUID().IsEmpty())
            { _lootAnimationUntil = Now + 750; Status = "Looting " + corpse.GetName(); return SIBehaviorResult.Running; }
            if (!me.IsAllowedToLoot(corpse))
            { _looted[corpse.GetGUID()] = Now + 30000; Status = "Looted " + corpse.GetName(); return SIBehaviorResult.Success; }
            SkipUnlootableCorpse(corpse.GetGUID());
            return SIBehaviorResult.Failure;
        }

        bool TryLoot()
        {
            if (me.IsInCombat() || _goal is not ("quest" or "grind" or "guard" or "gather" or "professions")) return false;
            PruneExpired(_looted);
            Creature corpse = NearbyCreatures(40).Where(creature => !creature.IsAlive() && !_failedLoot.Contains(creature.GetGUID()) && me.IsAllowedToLoot(creature) &&
                _looted.GetValueOrDefault(creature.GetGUID()) <= Now && !IsLearnedRouteBlocked(creature.GetPosition())).OrderBy(me.GetExactDist).FirstOrDefault();
            if (corpse == null) return false;
            return LootCorpse(corpse) != SIBehaviorResult.Failure;
        }
    }
}

