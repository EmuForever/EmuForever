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
        ObjectGuid _lootTarget;
        long _nextLoot;
        int _lootFailures;
        long _lootAnimationUntil, _lootMoneyBefore;
        int _lootItemsBefore;

        void CloseLootAnimation()
        {
            if (_lootAnimationUntil == 0) return;
            _lootAnimationUntil = 0;
            me.GetSession().DoLootReleaseAll();
        }

        bool TickLootAnimation()
        {
            if (_lootAnimationUntil == 0) return false;
            if (!me.IsAlive() || me.IsInCombat() || me.IsBeingTeleported() || _goal == "pause")
            { CloseLootAnimation(); return false; }
            if (Now < _lootAnimationUntil) { Behavior = "Loot"; _task?.Suspend(Now); return true; }
            me.GetSession().LootOpenViewForSI();
            CloseLootAnimation();
            if (ItemsLooted > _lootItemsBefore || me.GetMoney() > _lootMoneyBefore)
            { _lootFailures = 0; NoteGameplayProgress(); EquipUpgrades(); }
            else if (++_lootFailures >= 1)
            {
                _failedLoot.Add(_lootTarget);
                Report("loot_failed", "Core could not transfer the remaining loot; skipping this corpse.");
            }
            return false;
        }

        SIBehaviorResult LootCorpse(Creature corpse)
        {
            if (corpse == null || corpse.IsAlive() || !me.IsAllowedToLoot(corpse)) return SIBehaviorResult.Success;
            if (me.IsInCombat()) return SIBehaviorResult.Running;
            if (_failedLoot.Contains(corpse.GetGUID()) || _looted.GetValueOrDefault(corpse.GetGUID()) > Now) return SIBehaviorResult.Success;
            if (_lootTarget != corpse.GetGUID()) { _lootTarget = corpse.GetGUID(); _lootFailures = 0; _nextLoot = 0; }
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
            ++_lootFailures;
            if (!me.IsAllowedToLoot(corpse))
            { _looted[corpse.GetGUID()] = Now + 30000; Status = "Looted " + corpse.GetName(); return SIBehaviorResult.Success; }
            if (_lootFailures >= 1)
            {
                _failedLoot.Add(corpse.GetGUID());
                Report("loot_failed", "Core could not transfer the remaining loot; skipping this corpse.");
                return SIBehaviorResult.Failure;
            }
            Status = "Looting " + corpse.GetName();
            return SIBehaviorResult.Running;
        }

        bool TryLoot()
        {
            if (me.IsInCombat() || _goal is not ("quest" or "grind" or "guard")) return false;
            foreach (ObjectGuid guid in _looted.Where(pair => pair.Value <= Now).Select(pair => pair.Key).ToArray()) _looted.Remove(guid);
            Creature corpse = NearbyCreatures(40).Where(creature => !creature.IsAlive() && !_failedLoot.Contains(creature.GetGUID()) && me.IsAllowedToLoot(creature) &&
                _looted.GetValueOrDefault(creature.GetGUID()) <= Now && !IsLearnedRouteBlocked(creature.GetPosition())).OrderBy(me.GetExactDist).FirstOrDefault();
            if (corpse == null) return false;
            return LootCorpse(corpse) != SIBehaviorResult.Failure;
        }
    }
}

