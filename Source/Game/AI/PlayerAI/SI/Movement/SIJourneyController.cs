using Framework.Constants;
using Game.Entities;
using Game.Spells;
using System;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        bool _longJourney;
        string _journeyReason;
        float _journeyArrival;
        bool _journeyRememberFailure=true;
        long _nextMountAttempt;
        Pet _travelPet;
        ReactStates _travelPetReaction;

        void EndJourney()
        {
            _longJourney = false;
            RestoreTravelPet();
        }

        void RestoreTravelPet()
        {
            if (_longJourney || _travelPet == null || me.IsMounted()) return;
            Pet pet = me.GetPet();
            // Flying mounts can temporarily unsummon the pet. Keep its original
            // reaction until the native dismount/resummon path has completed.
            if (pet == null) return;
            if (pet == _travelPet || pet.GetCharmInfo()?.GetPetNumber() == _travelPet.GetCharmInfo()?.GetPetNumber())
                pet.SetReactState(_travelPetReaction);
            _travelPet = null;
        }

        // Travel owns this interval: no new pulls, corpse detours, food/drink waits or
        // maintenance changes while commuting. Critical-health escape runs before this.
        bool ContinueLongJourney()
        {
            // Travel must not collect attackers until arrival. Hand the first
            // engagement back to normal combat, which can resume the quest afterward.
            if (_longJourney && (me.IsInCombat() || me.GetAttackers().Count != 0 || ActiveMobAttackers().Length != 0))
            { CancelTravel(); return false; }
            // The final neighborhood is local work, and grinding never skips
            // that work as a passive commute. Preserve its existing task.
            if(_longJourney&&!AvoidAggroOnLongJourney){EndJourney();return false;}
            if (!_longJourney || !_hasMovementDestination || _encounter.Active ||
                _goal is not ("quest" or "grind")) return false;
            if(HasJunk()&&NearbySellingVendor()!=null){EndJourney();return false;}
            // Entering a new settlement must not hide local quest givers behind
            // an indefinitely retained commute. Owned trainer/vendor visits stay owned.
            if(_goal=="quest"&&NearbyQuestInteraction())return false;
            // Yield at the existing acquisition radius, before entering the area.
            // Combat keeps its existing target/range checks; only the commute ends.
            if(_task?.Kind==SITaskKind.Objective&&NearbyCreatures(_settings.TargetSearchRadius).Any(c=>_task.Creatures.Contains(c.GetEntry())&&CanFight(c)&&TargetRetryReady(c)))
            {EndJourney();return false;}
            Pet pet = me.GetPet();
            if (pet?.IsAlive() == true)
            {
                if (_travelPet == null)
                { _travelPet = pet; _travelPetReaction = me.GetTemporaryPetReactState() ?? pet.GetReactState(); }
                pet.SetReactState(ReactStates.Passive);
                if (pet.GetVictim() != null) me.GetSession().PetFollowForSI(pet.GetVictim().GetGUID());
            }
            me.AttackStop(); _task?.Suspend(Now);
            Behavior = "Travel";
            SIBehaviorResult result = Travel(P(_movementDestination.Point), _journeyReason, _journeyArrival, mapId: _movementDestination.MapId, heightKnown: _movementDestination.HeightKnown,rememberFailure:_journeyRememberFailure);
            if (result == SIBehaviorResult.Running) return true;
            // A learned blocked route can fail without exhausting this follower.
            // Yield back to the task owner instead of retaining travel forever.
            EndJourney();
            return false;
        }

        static bool IsMountSpell(SpellInfo spell) => spell != null && spell.GetEffects().Any(effect => effect.IsAura(AuraType.Mounted));

        bool MountForTravel()
        {
            if (me.IsMounted() || !me.IsAlive() || me.IsInCombat() || _encounter.Active || me.GetTransport() != null ||
                me.IsInFlight() || SwimmingSurface(Here) || me.HasPlayerFlag(PlayerFlags.Ghost) || _goal == "flee" || Now < _nextMountAttempt) return false;
            _nextMountAttempt = Now + 10000;
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return true;
            if (me.GetStandState() != UnitStandStateType.Stand) me.SetStandState(UnitStandStateType.Stand);
            foreach (var spell in _knownSpells.Where(IsMountSpell).OrderByDescending(spell => spell.GetEffects()
                .Where(effect => effect.IsAura(AuraType.ModIncreaseMountedSpeed) || effect.IsAura(AuraType.ModIncreaseMountedFlightSpeed))
                .Select(effect => effect.BasePoints).DefaultIfEmpty(0).Max()))
                if (Cast(spell, me)) { _nextMountAttempt = Now + 1500; return true; }
            bool requested = false;
            me.ForEachItem(ItemSearchLocation.Inventory, item =>
            {
                foreach (var effect in item.GetEffects().Where(effect => effect.TriggerType == ItemSpelltriggerType.OnUse))
                {
                    SpellInfo spell = Global.SpellMgr.GetSpellInfo(effect.SpellID, me.GetMap().GetDifficultyID());
                    if (!IsMountSpell(spell) || !me.GetSession().UseItemForSI(item, spell.Id, me)) continue;
                    requested = true; return false;
                }
                return true;
            });
            if (requested) _nextMountAttempt = Now + 1500;
            return requested;
        }
    }
}

