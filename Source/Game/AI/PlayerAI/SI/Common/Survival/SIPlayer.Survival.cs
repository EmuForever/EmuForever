using Framework.Constants;
using Game.Entities;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    // Survival: learned avoidance (lethal creatures, blocked routes, failed interactions), escape,
    // food/drink and self-heals, resting, death recovery and corpse runs.
    public sealed partial class SIPlayer
    {
        readonly Dictionary<int, long> _dangerousEntries = new();
        readonly Dictionary<ObjectGuid, long> _dangerousTargets = new();
        readonly SIDestinationBlocks _blockedRoutes = new();
        readonly Dictionary<(int Quest, ObjectGuid Giver), long> _failedQuestInteractions = new();
        long _dangerUntil, _nextConsumableCheck;
        Position _fleeDestination;
        bool _resting;
        string _escapeReturnGoal = "quest";

        // Death recovery state
        long _deathAt, _ghostStartedAt, _nextReclaim;
        float _ghostDistance;
        Position _ghostOrigin, _lastGhostPosition, _reclaimPoint;
        readonly List<Position> _failedRecoveryPoints = new();
        long _ghostProgressAt, _nextCorpseRouteAttempt;
        int _failedCorpseRoutes;
        Position _ghostProgressPosition;
        public string Recovery { get; private set; } = "Alive";
        const float ReclaimTravelArrival = 1.25f;
        const float ReclaimReadyDistance = 1.5f;

        public static bool MustEscape(float health, bool combat, bool recovering) => combat && health <= .20f;

        Creature[] ActiveMobAttackers() => me.GetCombatManager().GetPvECombatRefs().Values.Select(reference => reference.GetOther(me))
            .Concat(me.GetAttackers()).OfType<Creature>().Where(creature => creature.IsAlive() && !creature.IsInEvadeMode())
            .DistinctBy(creature => creature.GetGUID()).ToArray();

        bool AvoidEnemy(Unit enemy) => enemy != null &&
            (_dangerousEntries.GetValueOrDefault(enemy.GetEntry()) > Now || _dangerousTargets.GetValueOrDefault(enemy.GetGUID()) > Now);

        void RememberLethalEnemies()
        {
            foreach (Unit attacker in me.GetAttackers().Append(me.GetVictim()).Where(unit => unit != null && unit.IsCreature()))
            {
                _dangerousTargets[attacker.GetGUID()] = Now + 120000;
                if (attacker.ToCreature().GetCreatureTemplate().Classification != CreatureClassifications.Normal)
                    _dangerousEntries[attacker.GetEntry()] = Now + 30 * 60 * 1000;
            }
            _dangerUntil = Now + 60000;
            if (!_operatorControlled && _goal == "flee") _goal = "quest";
        }

        void NoteGameplayProgress() { _lastActionProgress = Now; PruneLearnedAvoidance(); }

        static (int Map, int X, int Y) RouteCell(int map, Position point) =>
            (map, (int)MathF.Floor(point.GetPositionX() / 12), (int)MathF.Floor(point.GetPositionY() / 12));

        bool IsLearnedRouteBlocked(Position point) => point != null &&
            _blockedRoutes.Contains(me.GetMapId(), V(point), Now);

        void RememberBlockedRoute(Position destination, string reason)
        {
            bool known = IsLearnedRouteBlocked(destination);
            _blockedRoutes.Block(me.GetMapId(), V(destination), Now);
            if (!known) Report("navigation_blocked", reason);
            PruneLearnedAvoidance();
        }

        void PruneLearnedAvoidance()
        {
            _blockedRoutes.Prune(Now);
            foreach (var interaction in _failedQuestInteractions.Where(pair => pair.Value <= Now).Select(pair => pair.Key).ToArray())
                _failedQuestInteractions.Remove(interaction);
            foreach (var entry in _dangerousEntries.Where(pair => pair.Value <= Now).Select(pair => pair.Key).ToArray())
                _dangerousEntries.Remove(entry);
            foreach (var target in _dangerousTargets.Where(pair => pair.Value <= Now).Select(pair => pair.Key).ToArray())
                _dangerousTargets.Remove(target);
        }

        bool MayRetryQuestInteraction(int quest, ObjectGuid giver) => _failedQuestInteractions.GetValueOrDefault((quest, giver)) <= Now;

        void FailQuestInteraction(int quest, WorldObject giver, string reason)
        {
            _failedQuestInteractions[(quest, giver.GetGUID())] = Now + 120000;
            Report("interaction_failed", reason);
        }

        // Recovery state, mob count and level do not start an escape. Only critical health does.
        void EmergencySurvival()
        {
            if (!me.IsAlive()) return;
            Creature[] attackers = ActiveMobAttackers();
            if (_goal == "flee" && me.GetHealthPct() > 20)
            {
                _goal = _escapeReturnGoal; _fleeDestination = null;
                me.StopMoving(); ClearRoute();
                return;
            }
            if (!MustEscape(me.GetHealthPct() / 100f, me.IsInCombat() || attackers.Length != 0, _recovering)) return;
            if (_goal == "flee") return;
            _escapeReturnGoal = _goal;
            _goal = "flee"; _fleeDestination = null; _dangerUntil = Now + 60000;
            me.CastStop(); me.AttackStop(); me.GetMotionMaster().Clear(); me.StopMoving();
            Report("escape_started", "Escaping current attackers");
        }

        void Flee()
        {
            me.AttackStop();
            if (!me.IsInCombat() || ActiveMobAttackers().Length == 0 && me.GetAttackers().Count == 0)
            {
                _fleeDestination = null; _recovering = true; Stop();
                if (_goal == "flee") _goal = _escapeReturnGoal;
                Status = "Escaped; recovering";
                return;
            }
            Status = "Escaping combat";
            if (_fleeDestination == null || me.GetDistance(_fleeDestination) < 3 || ((siPlayer)me).NavigationBlocked)
            {
                SIRecoveryHazard[] threats = RecoveryHazards();
                float dx = 0, dy = 0;
                foreach (SIRecoveryHazard threat in threats)
                {
                    float distance = Math.Max(1, me.GetExactDist2d(threat.Position));
                    dx += (me.GetPositionX() - threat.Position.GetPositionX()) / (distance * distance);
                    dy += (me.GetPositionY() - threat.Position.GetPositionY()) / (distance * distance);
                }
                float length = MathF.Sqrt(dx * dx + dy * dy);
                if (length < .001f) { dx = MathF.Cos(me.GetOrientation() + MathF.PI); dy = MathF.Sin(me.GetOrientation() + MathF.PI); }
                float heading = MathF.Atan2(dy, dx);
                _fleeDestination = null;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    float angle = heading + (attempt % 2 == 0 ? 1 : -1) * ((attempt + 1) / 2) * MathF.PI / 6;
                    Position ground = Ground(me.GetPositionX() + MathF.Cos(angle) * 45, me.GetPositionY() + MathF.Sin(angle) * 45, me.GetPositionZ());
                    if (ground == null || !SICorpseRecovery.IsSafe(ground, threats) || !SICorpseRecovery.AvoidsNewThreats(me.GetPosition(), ground, threats)) continue;
                    _fleeDestination = ground; break;
                }
            }
            if (_fleeDestination != null) Travel(_fleeDestination, "Escaping combat");
        }

        Position Ground(float x, float y, float z)
        {
            float groundZ = z, supportingFloor = MapConst.InvalidHeight;
            me.UpdateAllowedPositionZ(x, y, ref groundZ, ref supportingFloor);
            // UpdateAllowedPositionZ leaves Z unchanged when no floor exists. That is not
            // evidence of ground: accepting it invented corpse/flee points below the terrain.
            return float.IsFinite(supportingFloor) && supportingFloor > MapConst.InvalidHeight &&
                float.IsFinite(groundZ) && Math.Abs(groundZ - z) < 40 ? new Position(x, y, groundZ) : null;
        }

        // Emergency self-heals and shields at low health (cheapest cast time first).
        bool Survive()
        {
            if (me.GetHealthPct() > 45) return false;
            foreach (SpellInfo spell in _knownSpells.Where(info => IsHeal(info) || IsShield(info))
                .OrderBy(info => info.CastTimeEntry?.Base ?? 0))
                if (Cast(spell, me)) return true;
            return false;
        }

        // Classic food/drink use the obsolete total-regen auras (20/21) as well as periodic heal/energize and regen mods.
        static bool RestoresHealth(SpellEffectInfo effect) => effect.IsAura(AuraType.PeriodicHeal) ||
            effect.IsAura(AuraType.ModHealthRegen) || effect.IsAura(AuraType.ModTotalHealthRegen);
        static bool RestoresPower(SpellEffectInfo effect) => effect.IsAura(AuraType.PeriodicEnergize) ||
            effect.IsAura(AuraType.ModPowerRegen) || effect.IsAura(AuraType.ModTotalPowerRegen);

        bool HasUsefulRecoveryAura(bool fullRecovery=false)
        {
            if (me.GetStandState() != UnitStandStateType.Sit) return false;
            bool health = fullRecovery?me.GetHealth()<me.GetMaxHealth():me.GetHealthPct()<99;
            bool mana = me.GetMaxPower(PowerType.Mana)>0&&
                (fullRecovery?me.GetPower(PowerType.Mana)<me.GetMaxPower(PowerType.Mana):me.GetPowerPct(PowerType.Mana)<95);
            return me.GetAppliedAuras().Any(pair => pair.Value.IsPositive() && !pair.Value.GetBase().IsPermanent() &&
                !pair.Value.GetBase().GetSpellInfo().IsPassive() && pair.Value.GetBase().GetSpellInfo().GetEffects().Any(effect =>
                health && RestoresHealth(effect) ||
                mana && RestoresPower(effect)));
        }

        // Food, drink and other recovery items through the ordinary use-item path.
        bool TryRecoveryConsumable(bool fullRecovery=false)
        {
            if (Now < _nextConsumableCheck || !me.IsAlive() || me.IsInCombat() || me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return false;
            _nextConsumableCheck = Now + 2000;
            bool healthAura = false, manaAura = false;
            foreach (var pair in me.GetAppliedAuras())
            {
                if (!pair.Value.IsPositive() || pair.Value.GetBase().GetSpellInfo().IsPassive() || pair.Value.GetBase().IsPermanent()) continue;
                var effects = pair.Value.GetBase().GetSpellInfo().GetEffects();
                healthAura |= effects.Any(RestoresHealth); manaAura |= effects.Any(RestoresPower);
            }
            var needs = SIRecoveryConsumables.Needs(me.GetHealthPct(), me.GetPowerPct(PowerType.Mana),
                me.GetMaxPower(PowerType.Mana) > 0, healthAura, manaAura, fullRecovery?100:50);
            bool healthNeeded = needs.Health, manaNeeded = needs.Mana;
            if (!healthNeeded && !manaNeeded) return false;
            var candidates = new List<(Item Item, SpellInfo Spell)>();
            me.ForEachItem(ItemSearchLocation.Inventory, item =>
            {
                foreach (var effect in item.GetEffects().Where(effect => effect.TriggerType == ItemSpelltriggerType.OnUse))
                {
                    SpellInfo spell = Global.SpellMgr.GetSpellInfo(effect.SpellID, me.GetMap().GetDifficultyID());
                    if (spell != null && !spell.IsPassive()) candidates.Add((item, spell));
                }
                return true;
            });
            foreach (var (item, spell) in candidates)
            {
                bool heals = spell.GetEffects().Any(effect => RestoresHealth(effect));
                bool restoresMana = spell.GetEffects().Any(effect => RestoresPower(effect));
                if (!(heals && healthNeeded || restoresMana && manaNeeded)) continue;
                if (!me.GetSpellHistory().IsReady(spell, item.GetEntry()) || me.CanUseItem(item) != InventoryResult.Ok) continue;
                bool alreadyRecovering = me.GetAppliedAuras().Any(pair => pair.Value.GetBase().GetSpellInfo().Id == spell.Id);
                if (alreadyRecovering) continue;
                CancelTravel();
                if (me.IsMounted()) me.RemoveAurasByType(AuraType.Mounted);
                if (!me.GetSession().UseItemForSI(item, spell.Id)) continue;
                Report("consumable_started", $"Started recovery item {item.GetEntry()}");
                return true;
            }
            return false;
        }

        bool PowerRecovered(float health, float mana) =>
            me.GetHealthPct() >= health && (me.GetMaxPower(PowerType.Mana) == 0 || me.GetPowerPct(PowerType.Mana) >= mana);

        // After death or escape: rest to 99% health / 95% mana and restore buffs before resuming.
        bool Recuperate()
        {
            if (!_recovering || me.IsInCombat()) return false;
            Recovery = "Recuperating";
            CancelTravel();
            Status = "Recovering health, power and buffs";
            if (HasUsefulRecoveryAura())
            {
                // Eating must not prevent starting a drink (or the reverse). Native
                // item cooldowns still govern the next request; existing auras continue.
                TryRecoveryConsumable(); return true;
            }
            if (Buff()) return true;
            if (TryRecoveryConsumable()) return true;
            if (me.GetHealthPct() < 99)
                foreach (SpellInfo spell in _knownSpells.Where(IsHeal))
                    if (Cast(spell, me)) return true;
            if (!PowerRecovered(99, 95))
            {
                if (me.GetStandState() != UnitStandStateType.Sit) me.SetStandState(UnitStandStateType.Sit);
                return true;
            }
            if (me.GetStandState() != UnitStandStateType.Stand) me.SetStandState(UnitStandStateType.Stand);
            if (Buff()) return true;
            _recovering = false; _reclaimPoint = null; Recovery = "Alive";
            if (_goal == "rest" && !_operatorControlled) _goal = "quest";
            NoteGameplayProgress();
            Report("recovery_complete", "Recovered health, power and buffs");
            return true;
        }

        // Use the same readiness thresholds as optional pulls; otherwise a rejected pull can never recover.
        bool Rest()
        {
            if (me.IsInCombat()) return false;
            bool usesMana = me.GetPowerType() == PowerType.Mana && me.GetMaxPower(PowerType.Mana) > 0;
            if (!_resting && (me.GetHealthPct() / 100f < SIPullSafety.MinimumHealth ||
                usesMana && me.GetPowerPct(PowerType.Mana) / 100f < SIPullSafety.MinimumMana)) _resting = true;
            // Ordinary recovery ends at pull readiness. Full recovery after death
            // remains in Recuperate; do not turn each fight into a wait for 95% mana.
            if (!_resting || SIPullSafety.RecoveryReady(me.GetHealthPct(), me.GetPowerPct(PowerType.Mana), usesMana))
            {
                _resting = false;
                if (me.GetStandState() == UnitStandStateType.Sit) me.SetStandState(UnitStandStateType.Stand);
                return false;
            }
            CancelTravel();
            Status = "Resting";
            if (HasUsefulRecoveryAura())
            {
                // Eating must not prevent starting a drink (or the reverse). Native
                // item cooldowns still govern the next request; existing auras continue.
                TryRecoveryConsumable(); return true;
            }
            if (Buff()) return true;
            if (TryRecoveryConsumable()) return true;
            if (me.GetHealthPct() < 80)
                foreach (SpellInfo spell in _knownSpells.Where(IsHeal))
                    if (Cast(spell, me)) return true;
            if (me.GetStandState() != UnitStandStateType.Sit) me.SetStandState(UnitStandStateType.Sit);
            return true;
        }

        // Release, run back as a ghost and reclaim at a safe point: within reclaim range, outside live hostiles'
        // aggro radius plus a margin, after an actual ghost run. Patrol positions are rechecked at arrival.
        void RecoverCorpse()
        {
            if (me.IsBeingTeleported()) return;
            if (!me.HasPlayerFlag(PlayerFlags.Ghost))
            {
                Recovery = "AwaitingRelease";
                if (_deathAt == 0)
                {
                    _deathAt = Now; _reclaimPoint = null; _failedRecoveryPoints.Clear(); _ghostStartedAt = 0; _ghostDistance = 0;
                    Stop(); RememberLethalEnemies();
                }
                if (Now - _deathAt >= 2000)
                {
                    me.GetSession().ReleaseSpirit();
                    Status = "Releasing spirit";
                }
                return;
            }
            if (_ghostStartedAt == 0)
            {
                _ghostStartedAt = _ghostProgressAt = Now; _ghostDistance = 0;
                _ghostOrigin = _ghostProgressPosition = new Position(me.GetPosition());
                _failedCorpseRoutes = 0;
            }
            if (_ghostProgressPosition.GetExactDist(me) >= 2)
            { _ghostProgressPosition = new Position(me.GetPosition()); _ghostProgressAt = Now; _failedCorpseRoutes = 0; }
            Corpse corpse = me.GetCorpse();
            if(corpse==null||corpse.GetMap()!=me.GetMap())
            {
                CancelTravel();Recovery="Blocked";Status="Waiting for a corpse on this map; spirit-healer resurrection is disabled";
                Report("recovery_blocked",Status);return;
            }
            // Last resort for a corpse the mesh cannot reach (a ledge, a sealed room, a broken tile): the ghost
            // is invisible to the living and cannot be harmed, so moving it to the corpse is the outcome a
            // successful run would have had. It still waits out the normal reclaim delay there.
            if(me.GetDistance(corpse)>SICorpseRecovery.ReclaimRadius&&(_failedCorpseRoutes>=2||Now-_ghostProgressAt>=90000))
            {
                CancelTravel();_failedCorpseRoutes=0;_ghostProgressAt=Now;_reclaimPoint=null;_failedRecoveryPoints.Clear();
                Report("corpse_recovery_fallback",$"Corpse route unavailable from {me.GetPosition()}; moving the ghost to the corpse");
                me.NearTeleportTo(corpse.GetPosition());
                return;
            }
            if(SICorpseRecovery.ShouldRetryCorpseRun(Now-_ghostProgressAt,_failedCorpseRoutes,true))
            {
                CancelTravel();_reclaimPoint=null;_failedRecoveryPoints.Clear();_failedCorpseRoutes=0;_ghostProgressAt=Now;
                _blockedRoutes.Unblock(me.GetMapId(),V(corpse.GetPosition()));
                _nextCorpseRouteAttempt=Now+5000;
            }
            if(Now<_nextCorpseRouteAttempt){Recovery="Blocked";Status="Retrying corpse route shortly; no spirit-healer resurrection";return;}
            if (_lastGhostPosition != null) _ghostDistance += me.GetExactDist(_lastGhostPosition);
            _lastGhostPosition = new Position(me.GetPosition());
            if (me.GetDistance(corpse) > 65)
            {
                Recovery = "CorpseRun";
                if (Travel(corpse.GetPosition(), "Returning to corpse", arrival: 30, slack: 25) == SIBehaviorResult.Failure)
                { ++_failedCorpseRoutes; CancelTravel(); _nextCorpseRouteAttempt=Now+5000; }
                return;
            }
            SIRecoveryHazard[] hazards = RecoveryHazards();
            if (_reclaimPoint != null && !SICorpseRecovery.IsSafe(_reclaimPoint, hazards)) { _reclaimPoint = null; Stop(); }
            if (_reclaimPoint == null)
                foreach (Position candidate in SICorpseRecovery.Candidates(corpse.GetPosition(), me.GetPosition(), hazards))
                {
                    if (_failedRecoveryPoints.Any(failed => failed.GetExactDist(candidate) < 3)) continue;
                    // Already standing in reclaim range is sufficient. Re-projecting this exact
                    // XY onto another floor can manufacture an impossible vertical corpse run.
                    Position ground = SICorpseRecovery.ResolveCandidate(candidate, me.GetPosition(),
                        point => Ground(point.GetPositionX(), point.GetPositionY(), point.GetPositionZ()));
                    if (ground == null || ground.GetExactDist(corpse) > SICorpseRecovery.ReclaimRadius || !SICorpseRecovery.IsSafe(ground, hazards)) continue;
                    _reclaimPoint = ground; break;
                }
            if (_reclaimPoint == null)
            {
                Recovery = "Blocked"; Stop();
                Status = "Waiting for a safe point near the corpse";
                // Patrols and evading enemies can make a previously blocked spot safe.
                if (Now >= _nextReclaim) { _nextReclaim = Now + 10000; _failedRecoveryPoints.Clear(); }
                return;
            }
            if (me.GetExactDist(_reclaimPoint) > ReclaimReadyDistance)
            {
                Recovery = "CorpseRun";
                if (Travel(_reclaimPoint, "Returning to a safe point near the corpse", ReclaimTravelArrival) == SIBehaviorResult.Failure &&
                    me.GetExactDist(_reclaimPoint) > ReclaimReadyDistance)
                { _failedRecoveryPoints.Add(_reclaimPoint); _reclaimPoint = null; }
                return;
            }
            if (!SICorpseRecovery.IsSafe(me.GetPosition(), hazards)) { _reclaimPoint = null; Stop(); return; }
            Recovery = "AwaitingReclaim"; me.StopMoving();
            Status = "Waiting for normal corpse reclaim";
            if (SICorpseRecovery.CanFinishCorpseRun(Now - _ghostStartedAt) && Now >= _nextReclaim)
            {
                _nextReclaim = Now + 1000;
                me.GetSession().ReclaimPlayerCorpse();
            }
        }

        void OnResurrected()
        {
            if (_deathAt != 0 || _ghostStartedAt != 0 || Recovery is "CorpseRun" or "AwaitingReclaim" or "SpiritHealer") _recovering = true;
            _deathAt = 0; _ghostStartedAt = 0; _lastGhostPosition = null; _reclaimPoint = null;
            _failedCorpseRoutes = 0; _ghostProgressPosition = null;
            Recovery = _recovering ? "Recuperating" : "Alive";
            BeginPreparation();
        }

        // Hearthstone through the ordinary item cast (cooldown, cast time, interruption).
        bool UseHearthstone(out string reason)
        {
            reason = null;
            if (!me.IsAlive() || me.IsInCombat()) { reason = "SI hearth travel requires an alive player out of combat."; return false; }
            Item hearth = null;
            me.ForEachItem(ItemSearchLocation.Inventory, item => { if (item.GetEntry() == 6948) { hearth = item; return false; } return true; });
            if (hearth == null) { reason = "The SI player has no hearthstone."; return false; }
            var effect = hearth.GetEffects().FirstOrDefault(value => value.TriggerType == ItemSpelltriggerType.OnUse);
            SpellInfo spell = effect != null ? Global.SpellMgr.GetSpellInfo(effect.SpellID, me.GetMap().GetDifficultyID()) : null;
            if (spell == null || !me.GetSpellHistory().IsReady(spell, hearth.GetEntry())) { reason = "Hearthstone is on cooldown."; return false; }
            Stop();
            if (!me.GetSession().UseItemForSI(hearth, spell.Id)) { reason = "World rejected the hearthstone cast."; return false; }
            Report("hearth_started", "Using hearthstone");
            return true;
        }
    }
}

