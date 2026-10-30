using Framework.Constants;
using Game.Entities;
using Game.DataStorage;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        bool _longJourney;
        string _journeyReason;
        float _journeyArrival;
        bool _journeyRememberFailure=true;
        long _nextMountAttempt;
        readonly SITravelMount _travelMount = new();
        Pet _travelPet;
        ReactStates _travelPetReaction;

        void EndJourney()
        {
            _longJourney = false;
            RestoreTravelPet();
        }

        // A commuting pet follows passively; its own reaction is restored when travel ends.
        void PacifyTravelPet()
        {
            Pet pet = me.GetPet();
            if (pet?.IsAlive() != true) return;
            if (_travelPet == null)
            { _travelPet = pet; _travelPetReaction = me.GetTemporaryPetReactState() ?? pet.GetReactState(); }
            pet.SetReactState(ReactStates.Passive);
            if (pet.GetVictim() is Unit victim) me.GetSession().PetFollowForSI(victim.GetGUID());
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
            // The service task owns its destination and any taxi itinerary. Route
            // interests and social/city replans must not replace it mid-commute.
            if (OwnedServiceTrip || _journeyFlight!=null) return false;
            if(_longJourney&&StoragePressure()){EndJourney();return false;}
            if (_longJourney && (me.GetHealthPct() < SIProvisionPolicy.HealthThreshold(me.GetClass()) ||
                me.GetPowerType() == PowerType.Mana && me.GetPowerPct(PowerType.Mana) < SIProvisionPolicy.ManaThreshold(me.GetClass())))
            { EndJourney(); return false; }
            // Travel must not collect attackers until arrival. Hand the first
            // engagement back to normal combat, which can resume the quest afterward.
            if (_longJourney && (me.IsInCombat() || me.GetAttackers().Count != 0 || ActiveMobAttackers().Length != 0))
            { CancelTravel(); return false; }
            // The final neighborhood is local work, and grinding never skips
            // that work as a passive commute. Preserve its existing task.
            if(_longJourney&&!AvoidAggroOnLongJourney){EndJourney();return false;}
            if (!_longJourney || !_hasMovementDestination || _encounter.Active ||
                !WorkingGoal) return false;
            if(HasJunk()&&NearbySellingVendor()!=null){EndJourney();return false;}
            // The route was picked to pass this turn-in or trainer; stop for it.
            if(!CommittedToJourney()&&NearUsefulStop()){EndJourney();_nextTrainingScan=0;return false;}
            // Entering a new settlement must not hide local quest givers behind
            // an indefinitely retained commute. Owned trainer/vendor visits stay owned.
            if(_goal=="quest"&&NearbyQuestInteraction())return false;
            // Yield at the existing acquisition radius, before entering the area.
            // Combat keeps its existing target/range checks; only the commute ends.
            if(_task?.Kind==SITaskKind.Objective&&NearbyCreatures(_settings.TargetSearchRadius).Any(c=>_task.Creatures.Contains(c.GetEntry())&&CanFight(c)&&TargetRetryReady(c)))
            {EndJourney();return false;}
            PacifyTravelPet();
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

        bool WaitingForTravelMount() => _travelMount.Waiting(Now, me.IsMounted(), me.HasAuraType(AuraType.Mounted),
            IsMountSpell(me.GetCurrentSpell(CurrentSpellTypes.Generic)?.GetSpellInfo()),
            me.IsAlive() && !me.IsInCombat() && !_encounter.Active && !me.IsInFlight() &&
            me.GetTransport() == null && _goal != "flee" && !me.HasPlayerFlag(PlayerFlags.Ghost));

        void TravelMountRequested(SpellInfo spell)
        {
            _travelMount.Begin(Now, spell.CastTimeEntry?.Base ?? 0);
            _nextMountAttempt = Now + 10000;
            // Collection mounts must finish casting before movement resumes.
            HaltSpline();
        }

        bool MountForTravel()
        {
            if (WaitingForTravelMount()) return true;
            if (me.IsMounted() || !me.IsAlive() || me.IsInCombat() || _encounter.Active || me.GetTransport() != null ||
                me.IsInFlight() || SwimmingSurface(Here) || me.HasPlayerFlag(PlayerFlags.Ghost) || _goal == "flee" || Now < _nextMountAttempt) return false;
            if(!_travelMount.CanAttempt(Now,me.IsOutdoors(),SwimmingSurface(Here)))return false;
            if(me.GetSkillValue(SkillType.Riding)==0)return false;
            _nextMountAttempt = Now + 10000;
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return true;
            StandUp();
            var collection=me.GetSession().GetCollectionMgr();
            if(collection==null)return false;
            foreach (var spell in collection.GetAccountMounts().Keys
                .Select(id=>Global.SpellMgr.GetSpellInfo(id,me.GetMap().GetDifficultyID()))
                .Where(UsableCollectionMount).OrderByDescending(spell => spell.GetEffects()
                .Where(effect => effect.IsAura(AuraType.ModIncreaseMountedSpeed) || effect.IsAura(AuraType.ModIncreaseMountedFlightSpeed))
                .Select(effect => effect.BasePoints).DefaultIfEmpty(0).Max()))
                if (Cast(spell, me)) { TravelMountRequested(spell); return true; }
            return false;
        }

        long _nextMountValidation;
        void ValidateTravelMount()
        {
            if(Now<_nextMountValidation||me.IsInFlight()||me.GetTransport()!=null||me.GetVehicle()!=null)return;
            _nextMountValidation=Now+1000;
            var mounts=me.GetAuraEffectsByType(AuraType.Mounted);
            if(mounts.Count!=0&&!mounts.Any(effect=>UsableCollectionMount(effect.GetSpellInfo())))
                me.RemoveAurasByType(AuraType.Mounted);
        }

        bool UsableCollectionMount(SpellInfo spell)
        {
            if(!IsMountSpell(spell)||me.GetSkillValue(SkillType.Riding)==0||
                me.GetSession().GetCollectionMgr()?.GetAccountMounts().ContainsKey(spell.Id)!=true)return false;
            var mount=Global.DB2Mgr.GetMount(spell.Id);
            if(mount==null||!me.IsSpellFitByClassAndRace(spell.Id))return false;
            var family=CliDB.ChrClassesStorage.LookupByKey(me.GetClass())?.SpellClassSet;
            if(spell.SpellFamilyName!=SpellFamilyNames.Generic&&spell.SpellFamilyName!=family)return false;
            if(mount.PlayerConditionID!=0)
            {
                var condition=CliDB.PlayerConditionStorage.LookupByKey(mount.PlayerConditionID);
                if(condition==null||!ConditionManager.IsPlayerMeetingCondition(me,condition))return false;
            }
            int type=mount.MountTypeID!=0?mount.MountTypeID:
                spell.GetEffects().First(effect=>effect.IsAura(AuraType.Mounted)).MiscValueB;
            var capability=me.GetMountCapability(type);
            if(capability==null)return false;
            if(capability.PlayerConditionID!=0)
            {
                var condition=CliDB.PlayerConditionStorage.LookupByKey(capability.PlayerConditionID);
                if(condition==null||!ConditionManager.IsPlayerMeetingCondition(me,condition))return false;
            }
            return true;
        }
    }
}

