using Framework.Constants;
using Game.Entities;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI
{
    // Port of EmuForever SIClassCombatController + SIPullSafety + the class-aware parts of SIPlayerController.Combat.
    public sealed partial class SIPlayer
    {
        readonly Dictionary<string, SpellInfo[]> _classSpells = new(StringComparer.Ordinal);
        SpellInfo[] _knownSpells = Array.Empty<SpellInfo>();
        readonly Dictionary<(string Rule, ObjectGuid Target), long> _classRetryAt = new();
        readonly Dictionary<int, long> _castReportAt = new();
        int _spellCount = -1;
        ulong _spellBookRevision=ulong.MaxValue;
        int _spellBookDifficulty=-1;
        readonly SIRoutineExecution _routine = new();
        long _nextRoutineAction;
        SpellInfo _lastRoutineSpell;
        SICombatRoutine ClassRoutine => SIRoutineManager.For(me.GetClass());

        long Now => (long)_clock.TotalMilliseconds;

        static string SpellName(SpellInfo spell) => spell.SpellName[Locale.enUS].Trim().ToLowerInvariant();

        static SpellInfo[] HighestKnownRanks(IEnumerable<SpellInfo> spells) => spells
            .GroupBy(info => info.GetFirstRankSpell().Id)
            .Select(group => group.OrderByDescending(info => info.GetRank()).ThenByDescending(info => info.SpellLevel).First())
            .OrderByDescending(info => info.SpellLevel).ToArray();

        void RefreshSpellBook()
        {
            int count = me.GetSpellMap().Count;
            Difficulty difficulty = me.GetMap().GetDifficultyID();
            if (count == _spellCount && me.SpellBook.Revision == _spellBookRevision && (int)difficulty == _spellBookDifficulty) return;
            _spellCount = count; _spellBookRevision = me.SpellBook.Revision; _spellBookDifficulty = (int)difficulty;
            _knownSpells = HighestKnownRanks(me.GetSpellMap().Where(pair => pair.Value.Active && !pair.Value.Disabled && pair.Value.State != PlayerSpellState.Removed)
                .Select(pair => Global.SpellMgr.GetSpellInfo(pair.Key, difficulty))
                .Where(info => info != null && !info.IsPassive() && info.GetEffects().Count != 0));
            _classSpells.Clear();
            // All learned ranks under one name, strongest first.
            foreach (var group in _knownSpells.GroupBy(SpellName))
                if (group.Key.Length != 0)
                    _classSpells[group.Key] = group.OrderByDescending(info => info.GetRank())
                        .ThenByDescending(info => info.GetEffects().Sum(effect => Math.Abs(effect.BasePoints)))
                        .ThenByDescending(info => info.Id).ToArray();
            NormalizeExclusivePaladinBuffs();
        }

        void NormalizeExclusivePaladinBuffs()
        {
            if (me.GetClass() != Class.Paladin) return;
            // Clean up saved stacks from the old routine once at login/spellbook
            // change. Snapshot before removals; party auras from other casters
            // remain valid and must not be stripped.
            var extras = me.GetOwnedAuras().Select(pair => pair.Value)
                .Where(aura => aura.GetCasterGUID() == me.GetGUID() &&
                    aura.GetSpellInfo().GetSpellSpecific() is SpellSpecificType.Seal or SpellSpecificType.Aura)
                .GroupBy(aura => aura.GetSpellInfo().GetSpellSpecific())
                .SelectMany(group => group.OrderByDescending(aura => aura.GetSpellInfo().SpellLevel)
                    .ThenByDescending(aura => aura.GetSpellInfo().GetRank()).Skip(1)).ToArray();
            foreach (var aura in extras) aura.Remove();
        }

        static bool HasNamedAura(Unit target, string name) =>
            target.GetAppliedAuras().Any(pair => SpellName(pair.Value.GetBase().GetSpellInfo()) == name);

        static bool IsHeal(SpellInfo spell) => !WarlockRoutine.IsHealthFunnel(spell) &&
            spell.GetEffects().Any(effect => effect.Effect is SpellEffectName.Heal or SpellEffectName.HealMaxHealth ||
                effect.IsAura(AuraType.PeriodicHeal));
        static bool IsShield(SpellInfo spell) => spell.GetEffects().Any(effect => effect.IsAura(AuraType.SchoolAbsorb));
        static bool DirectDamageEffect(SpellEffectInfo effect) => effect.Effect is SpellEffectName.SchoolDamage or SpellEffectName.HealthLeech or
            SpellEffectName.WeaponDamageNoSchool or SpellEffectName.WeaponPercentDamage or SpellEffectName.WeaponDamage or SpellEffectName.NormalizedWeaponDmg;
        static bool DamageEffect(SpellEffectInfo effect) => DirectDamageEffect(effect) ||
            effect.IsAura(AuraType.PeriodicDamage) || effect.IsAura(AuraType.PeriodicLeech);
        // Single-target damage only: area spells would pull everything around the target.
        static bool IsDamage(SpellInfo spell) => spell.GetEffects().Any(DamageEffect) &&
            !spell.GetEffects().Any(effect => effect.HasRadius(SpellTargetIndex.TargetA) || effect.HasRadius(SpellTargetIndex.TargetB));
        static bool HasDirectDamage(SpellInfo spell) => spell.GetEffects().Any(DirectDamageEffect);

        // Fireball/Regrowth/Judgement can be used again while their secondary aura persists.
        // Pure aura actions and explicit DoT rules still wait for the aura to expire.
        static bool AuraBlocksRecast(SpellInfo spell, bool missingAuraIntent) => missingAuraIntent ||
            !HasDirectDamage(spell) && !spell.GetEffects().Any(effect => effect.Effect is SpellEffectName.Heal or SpellEffectName.HealMaxHealth);

        float DamageScore(SIClassStrategy strategy, SpellInfo spell)
        {
            float damage = spell.GetEffects().Where(DamageEffect).Sum(effect => Math.Max(1, Math.Abs(effect.CalcValue(me))));
            float castSeconds = spell.CastTimeEntry != null ? spell.CastTimeEntry.Base / 1000f : 0;
            float score = damage / Math.Max(1, castSeconds + 1.5f);
            if ((spell.SchoolMask & strategy.PreferredSchool) != 0) score *= 1.2f;
            return score;
        }

        SIClassStrategy ActiveClassStrategy()
        {
            SIClassStrategy strategy = SIClassStrategy.For(me.GetClass(), (byte)me.GetShapeshiftForm());
            // An enhancement shaman should close for its actual learned attacks.
            if (me.GetClass() == Class.Shaman && (_classSpells.ContainsKey("stormstrike") || _classSpells.ContainsKey("lava lash")))
                return strategy with { Melee = true, CombatRange = 3 };
            return strategy;
        }

        Unit _previousCombatTarget;
        Position _previousCombatTargetPosition;

        SICombatFacts ClassCombatFacts(Unit target)
        {
            Pet pet = me.GetPet();
            bool petDead = pet != null && !pet.IsAlive();
            float petHealth = pet != null ? pet.GetHealthPct() / 100f : 1;
            float enemyHealth = 1, distance = 0, melee = 5;
            bool castingEnemy = false, movingEnemy = false, behind = false, rootedEnemy = false, enemyBuff = false;
            if (target != null)
            {
                distance = me.GetDistance(target);
                melee = me.GetMeleeRange(target);
                enemyHealth = target.GetHealthPct() / 100f;
                castingEnemy = target.IsNonMeleeSpellCast(false);
                behind = !target.HasInArc(MathF.PI, me);
                movingEnemy = target.IsMoving() || _previousCombatTarget == target && _previousCombatTargetPosition != null &&
                    _previousCombatTargetPosition.GetExactDist(target) > .1f;
                _previousCombatTarget = target; _previousCombatTargetPosition = new Position(target.GetPosition());
                rootedEnemy = target.HasAuraType(AuraType.ModRoot) || target.HasAuraType(AuraType.ModStun);
                enemyBuff = target.GetAppliedAuras().Any(pair => pair.Value.IsPositive() && pair.Value.GetBase().GetSpellInfo().Dispel != DispelType.None);
            }
            bool usesMana = me.GetMaxPower(PowerType.Mana) > 0;
            return new SICombatFacts(me.IsInCombat(), me.GetHealthPct() / 100f,
                usesMana ? me.GetPowerPct(PowerType.Mana) / 100f : 1,
                me.GetPower(PowerType.Rage), me.GetPower(PowerType.Energy), me.GetPower(PowerType.ComboPoints),
                enemyHealth, distance, melee, behind, me.HasAuraType(AuraType.ModStealth),
                castingEnemy, movingEnemy, rootedEnemy, me.GetAttackers().Count, pet != null, petDead, petHealth,
                me.HasAuraType(AuraType.ModFear) || me.HasUnitState(UnitState.Fleeing), me.HasUnitState(UnitState.Root),
                me.GetAppliedAuras().Any(pair => !pair.Value.IsPositive() && pair.Value.GetBase().GetSpellInfo().Dispel != DispelType.None),
                enemyBuff, (byte)me.GetShapeshiftForm());
        }

        bool CanDispelWith(SpellInfo spell, Unit target, bool negative) =>
            spell.GetEffects().Where(effect => effect.Effect == SpellEffectName.Dispel).Any(effect =>
                target.GetAppliedAuras().Any(pair => pair.Value.IsPositive() != negative &&
                    pair.Value.GetBase().GetSpellInfo().Dispel != DispelType.None &&
                    (effect.MiscValue == 0 || effect.MiscValue == (int)pair.Value.GetBase().GetSpellInfo().Dispel)));

        // Ordinary CheckCast validates power, cooldown, reagents, range, sight and the spell's target.
        bool Cast(SpellInfo spell, Unit target)
        {
            if (WarlockRoutine.IsHealthFunnel(spell) && !CanHealthFunnelAt(target)) return false;
            if (spell != null && spell.IsRangedWeaponSpell() && HunterNeedsMelee(target)) return false;
            if (spell == null || target == null || !me.IsAlive() || me.IsNonMeleeSpellCast(false, skipAutorepeat: true) || !me.HasSpell(spell.Id) ||
                me.GetSpellHistory().HasGlobalCooldown(spell) || !me.GetSpellHistory().IsReady(spell) || !CanResourceCast(spell)) return false;
            if (BreaksMagePolymorph(spell, target)) return false;
            if (target != me && !spell.HasAttribute(SpellAttr2.IgnoreLineOfSight) && !CombatSight(target)) return false;
            if (spell.GetEffects().All(effect => effect.IsAura()) && target.HasAura(spell.Id, me.GetGUID())) return false;
            Spell cast = new(me, spell, TriggerCastFlags.None);
            if (cast.CheckUnitTargetRange(target) != SpellCastResult.SpellCastOk) return false;
            bool castTime = spell.CastTimeEntry != null && spell.CastTimeEntry.Base > 0;
            SpellCastResult check = cast.CheckPetCast(target);
            // A cast-time spell that only fails because the bot is walking stops the walk and checks again.
            if (castTime && me.IsMoving() && check is SpellCastResult.SpellCastOk or SpellCastResult.Moving or SpellCastResult.UnitNotInfront)
            {
                me.GetMotionMaster().Clear();
                me.StopMoving();
                check = cast.CheckPetCast(target);
            }
            if (check == SpellCastResult.NotStanding && target == me && !HasUsefulRecoveryAura())
            {
                StandUp();
                check = cast.CheckPetCast(target);
            }
            if (check == SpellCastResult.UnitNotInfront && !me.HasUnitState(UnitState.CannotTurn))
            {
                me.SetFacingToObject(target);
                check = cast.CheckPetCast(target);
            }
            if (!SISpellCastPolicy.Accept(check))
            {
                int key = spell.Id;
                if (target != me && IsDamage(spell) && Now >= _castReportAt.GetValueOrDefault(key))
                {
                    _castReportAt[key] = Now + 5000;
                    Report("cast_rejected", $"{SpellName(spell)}: {check}");
                }
                return false;
            }
            if (castTime) me.StopMoving();
            StandUp();
            DoCastAtTarget((cast, target));
            _nextRoutineAction = Now + 250;
            _lastRoutineSpell = spell;
            return true;
        }

        bool CanHealthFunnelAt(Unit target)
        {
            Pet pet = me.GetPet();
            return target != null && target == pet && me.IsAlive() &&
                WarlockRoutine.CanHealthFunnel(pet != null, pet?.IsAlive() == true,
                    pet?.GetHealthPct() / 100f ?? 1, me.GetHealthPct() / 100f);
        }

        void StopUnsafeHealthFunnel()
        {
            if (me.GetClass() != Class.Warlock) return;
            Spell channel = me.GetCurrentSpell(CurrentSpellTypes.Channeled);
            if (channel == null || !WarlockRoutine.IsHealthFunnel(channel.GetSpellInfo())) return;
            Pet pet = me.GetPet();
            if (!CanHealthFunnelAt(pet) || channel.m_targets.GetUnitTargetGUID() != pet.GetGUID())
                me.InterruptSpell(CurrentSpellTypes.Channeled);
        }

        bool TryClassAction(SICombatActionStage stage, Unit enemy = null)
        {
            if (!me.IsAlive() || me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return false;
            SICombatFacts facts = ClassCombatFacts(enemy);
            foreach (SICombatRule rule in ClassRoutine.ForStage(stage))
            {
                if (stage == SICombatActionStage.PreCombatBuff &&
                    (facts.Combat || rule.Target != SICombatActionTarget.Self || !rule.MissingAura &&
                     (rule.Conditions & (SICombatCondition.PetAbsent | SICombatCondition.PetDead)) == 0)) continue;
                if (!facts.Matches(rule.Conditions)) continue;
                if (rule.RequiredAura != null && !HasNamedAura(me, rule.RequiredAura)) continue;
                Unit target = rule.Target switch
                {
                    SICombatActionTarget.Self => me,
                    SICombatActionTarget.Pet => me.GetPet(),
                    _ => enemy
                };
                if (target == null || _classRetryAt.GetValueOrDefault((rule.Name, target.GetGUID())) > Now) continue;
                // Alternatives represent one intent: do not cast a second seal, armor or stance.
                if (rule.MissingAura && rule.Alternatives.Any(name => HasCurrentBuff(target, name))) continue;
                foreach (string alternative in rule.Alternatives)
                {
                    if (!_classSpells.TryGetValue(alternative, out SpellInfo[] ranks)) continue;
                    foreach (SpellInfo spell in ranks)
                    {
                        if ((rule.Conditions & SICombatCondition.SelfDebuff) != 0 && !CanDispelWith(spell, me, negative: true)) continue;
                        if ((rule.Conditions & SICombatCondition.EnemyBuff) != 0 && !CanDispelWith(spell, target, negative: false)) continue;
                        if (HasCurrentBuff(target, alternative) && AuraBlocksRecast(spell, rule.MissingAura)) continue;
                        if (Cast(spell, target))
                        {
                            _classRetryAt[(rule.Name, target.GetGUID())] = Now + rule.RetryMilliseconds;
                            Report("combat_strategy", $"{rule.Name}: {alternative} (spell {spell.Id}, rank {spell.GetRank()})");
                            return true;
                        }
                    }
                }
                _classRetryAt[(rule.Name, target.GetGUID())] = Now + 750;
            }
            if (_classRetryAt.Count > 512)
                PruneExpired(_classRetryAt);
            return false;
        }

        // Interrupt/dispel/control reactions are separate from healing and damage rotations.
        bool ClassReaction(Unit target) => TryClassAction(SICombatActionStage.Reaction, CanFight(target) ? target : null);

        bool HasCurrentBuff(Unit target, string name) => target.GetAppliedAuras().Any(pair =>
        {
            SpellInfo active = pair.Value.GetBase().GetSpellInfo();
            return SpellName(active) == name && BuffRankIsCurrent(active, _knownSpells);
        });

        // Some upgrades change names within one rank chain (e.g. Frost Armor -> Ice Armor).
        static bool BuffRankIsCurrent(SpellInfo active, IEnumerable<SpellInfo> learned) => learned.All(spell =>
            spell.GetFirstRankSpell().Id != active.GetFirstRankSpell().Id || spell.GetRank() <= active.GetRank());

        // Long-lived maintenance auras only; short cooldown defensives and heals belong to combat/recovery.
        static bool IsMaintenanceBuff(SpellInfo spell) => spell.IsPositive() && !spell.IsChanneled() &&
            !IsHeal(spell) && !IsShield(spell) &&
            SISelfBuffPolicy.Sustainable(spell.GetDuration(), spell.GetRecoveryTime(), spell.ProcCharges) &&
            spell.GetEffects().Count > 0 && spell.GetEffects().All(effect => effect.IsAura()) &&
            !spell.HasAura(AuraType.Mounted) && !spell.HasAura(AuraType.ModShapeshift) && !spell.HasAura(AuraType.ModStealth);

        // Out-of-combat maintenance: one successful cast per tick, through normal spell validation.
        long _nextBuffAction;
        bool ConflictsWithActiveBuff(SpellInfo spell) => me.GetAppliedAuras().Any(pair =>
        {
            var active=pair.Value.GetBase();
            var info=active.GetSpellInfo();
            if(spell.IsAuraExclusiveBySpecificWith(info)||spell.IsAuraExclusiveBySpecificPerCasterWith(info))return true;
            var rule=Global.SpellMgr.CheckSpellGroupStackRules(spell,info);
            return rule is SpellGroupStackRule.Exclusive or SpellGroupStackRule.ExclusiveHighest ||
                rule==SpellGroupStackRule.ExclusiveFromSameCaster && active.GetCasterGUID()==me.GetGUID();
        });
        // Learned long-lived self buffs that the class routine does not manage and that are missing now.
        // Mutually exclusive armor, seals, blessings, aspects, etc. never replace one another.
        IEnumerable<SpellInfo> MissingMaintenanceBuffs() => _knownSpells.Where(spell => IsMaintenanceBuff(spell) &&
            !ClassRoutine.ManagedBuffs.Contains(SpellName(spell)) && !HasCurrentBuff(me, SpellName(spell)) && !ConflictsWithActiveBuff(spell));

        bool SelfBuffsPending()
        {
            var facts=ClassCombatFacts(null);
            foreach(var rule in ClassRoutine.ForStage(SICombatActionStage.PreCombatBuff))
            {
                if(!rule.MissingAura||rule.Target!=SICombatActionTarget.Self||!facts.Matches(rule.Conditions)||
                    rule.RequiredAura!=null&&!HasNamedAura(me,rule.RequiredAura))continue;
                if(rule.Alternatives.Any(name=>HasCurrentBuff(me,name)))continue;
                if(rule.Alternatives.Any(name=>_classSpells.ContainsKey(name)))return true;
            }
            return MissingMaintenanceBuffs().Any();
        }

        bool Buff()
        {
            if (me.IsInCombat())
                return false;
            // Finish an affordable buff sequence across its global cooldowns before
            // recovery starts. Already active food/drink is allowed to finish.
            if (HasUsefulRecoveryAura()) return false;
            if (Now < _nextBuffAction) return true;

            if (TryClassAction(SICombatActionStage.PreCombatBuff))
            { _nextBuffAction = Now + 1600; return true; }

            foreach (SpellInfo spell in MissingMaintenanceBuffs())
                if (Cast(spell, me))
                { _nextBuffAction = Now + 1600; return true; }
            return false;
        }

        float AggroRadius(Creature creature) => creature.GetAttackDistance(me);

        SIRecoveryHazard[] RecoveryHazards() => NearbyCreatures(90)
            .Where(c => c.IsAlive() && c.IsHostileTo(me) && c.HasReactState(ReactStates.Aggressive) && !c.IsCivilian() && !c.IsInEvadeMode() &&
                !c.HasUnitFlag(UnitFlags.NonAttackable | UnitFlags.Uninteractible))
            .Select(c => new SIRecoveryHazard(new Position(c.GetPosition()), AggroRadius(c))).ToArray();

        bool SafeToInitiate(Creature enemy) => SafeToInitiate(enemy, out _);

        bool SafeToInitiate(Creature enemy, out string reason, bool questTarget = false)
        {
            reason = Now < _dangerUntil ? "Recovering after danger" : "Target is not attackable";
            if (Now < _dangerUntil || !CanFight(enemy)) return false;
            bool manaUser = me.GetPowerType() == PowerType.Mana && me.GetMaxPower(PowerType.Mana) > 0;
            float mana = manaUser ? me.GetPowerPct(PowerType.Mana) / 100f : 1;
            SIPullAssessment Readiness(int adds) => SIPullSafety.Assess(me.GetLevel(), enemy.GetLevel(), enemy.GetCreatureTemplate().Classification,
                me.GetHealthPct() / 100f, mana, manaUser, adds, AvoidEnemy(enemy), _recovering || _resting, questTarget);
            var ready = Readiness(0);
            if (!ready.Allowed) { reason = ready.Reason; return false; }
            // Evaluate where we would fight, not every creature in a busy starter area. A ranged pull
            // brings one enemy back; a melee approach must not walk into another hostile's aggro.
            float range = CombatApproachRange(enemy, false);
            Vector3 away = Here - V(enemy.GetPosition());
            float distance = new Vector2(away.X, away.Y).Length();
            Vector3 contact = distance > range && distance > .1f ? V(enemy.GetPosition()) + away * (range / distance) : Here;
            var threats = ActiveMobAttackers().Where(other => other != enemy).Select(other => other.GetGUID()).ToHashSet();
            foreach (Creature other in NearbyCreatures(_settings.TargetSearchRadius))
                if (other != enemy && other.IsAlive() && other.IsHostileTo(me) && CanFight(other) &&
                    !other.IsInEvadeMode() && !other.IsCivilian() && Vector3.Distance(V(other.GetPosition()), contact) < AggroRadius(other) + 2 &&
                    me.GetMap().IsInLineOfSight(me.GetPhaseShift(), contact.X, contact.Y, contact.Z + 1.5f,
                        other.GetPositionX(), other.GetPositionY(), other.GetPositionZ() + 1.5f, LineOfSightChecks.All, ModelIgnoreFlags.Nothing))
                    threats.Add(other.GetGUID());
            int additional = threats.Count;
            SIPullAssessment assessment = Readiness(additional);
            reason = assessment.Reason;
            return assessment.Allowed;
        }

        bool ApproachSpellAvailable(SpellInfo spell,Unit target)
        {
            if(!me.GetSpellHistory().IsReady(spell)||!CanResourceCast(spell)||
                spell.CheckTarget(me,target,false)!=SpellCastResult.SpellCastOk)return false;
            float minimum=me.GetSpellMinRangeForTarget(target,spell);
            if(spell.RangeEntry?.HasFlag(SpellRangeFlag.Melee)!=true)
            {
                if(spell.RangeEntry?.HasFlag(SpellRangeFlag.Ranged)==true)minimum+=me.GetMeleeRange(target);
                else if(minimum>0)minimum+=me.GetCombatReach()+target.GetCombatReach();
            }
            // A charge inside its minimum range or Exorcism against the wrong
            // creature type cannot justify standing at that spell's max range.
            return me.GetExactDist(target)>=minimum;
        }

        float CombatApproachRange(Unit target, bool forceMelee)
        {
            SIClassStrategy strategy = ActiveClassStrategy();
            float melee = Math.Max(1, me.GetMeleeRange(target) - .5f);
            if (forceMelee || HunterNeedsMelee(target) || me.GetPowerType() == PowerType.Mana && me.GetPowerPct(PowerType.Mana) <= 10) return melee;
            // Melee routines can initiate with Charge, Death Grip or a learned ranged pull.
            // Use that spell's actual range before reverting to melee once it is on cooldown.
            if (!me.IsInCombat())
            {
                SICombatFacts facts = ClassCombatFacts(target);
                float pullRange = ClassRoutine.ForStage(SICombatActionStage.Pull)
                    .Where(rule => facts.Matches(rule.Conditions))
                    .SelectMany(rule => rule.Alternatives).Where(_classSpells.ContainsKey)
                    .SelectMany(name => _classSpells[name]).Where(spell => ApproachSpellAvailable(spell,target))
                    .Select(spell => me.GetSpellMaxRangeForTarget(target,spell) - 1).DefaultIfEmpty(melee).Max();
                if (pullRange > melee) return Math.Min(35, pullRange);
            }
            if (strategy.Melee) return melee;
            float maxRange = OffensiveSpells(target).Where(spell => me.GetSpellMaxRangeForTarget(target,spell) > 8 && ApproachSpellAvailable(spell,target))
                .Select(spell => me.GetSpellMaxRangeForTarget(target,spell) - 1).DefaultIfEmpty(melee).Max();
            return Math.Max(melee, Math.Min(strategy.CombatRange, maxRange));
        }

        IEnumerable<SpellInfo> OffensiveSpells(Unit target)
        {
            SIClassStrategy strategy = ActiveClassStrategy();
            float mana = me.GetMaxPower(PowerType.Mana) > 0 ? me.GetPowerPct(PowerType.Mana) / 100f : 1;
            return _knownSpells.Where(info => (IsDamage(info) || info.Id is 75 or 5019) && CanResourceCast(info) &&
                (HasDirectDamage(info) || !target.HasAura(info.Id, me.GetGUID())))
                .Select(info => (Info: info, Score: SIClassSpellPriorities.ScoreNamed(strategy, SpellName(info), DamageScore(strategy, info),
                    me.GetPower(PowerType.ComboPoints), target.GetHealthPct() / 100f, mana, me.GetHealthPct() / 100f)))
                .Where(value => float.IsFinite(value.Score)).OrderByDescending(value => value.Score).Select(value => value.Info);
        }

        // Range decisions must use attacks we can actually perform. CheckCast remains authoritative.
        bool CanResourceCast(SpellInfo spell) => (!spell.IsRangedWeaponSpell() || HasAmmunition()) && me.HasItemFitToSpellRequirements(spell) &&
            spell.CalcPowerCost(me, spell.SchoolMask).All(cost => cost.Power is PowerType.RuneBlood or PowerType.RuneFrost or PowerType.RuneUnholy ||
                (cost.Power == PowerType.Health
                ? me.GetHealth() > cost.Amount : me.GetPower(cost.Power) >= cost.Amount));

        // Pull and combat share the normal player attack/spell APIs. Always enable melee autoattack:
        // even a ranged character must swing when the enemy reaches it and a spell cannot be cast.
        SIBehaviorResult AttackEncounterTarget(Unit target)
        {
            if (!CanFight(target)) return SIBehaviorResult.Failure;
            // A moving opponent or newly changed cooldown can invalidate the
            // encounter's earlier range observation. Recheck before attacks,
            // including pet commands, rather than repeatedly casting from here.
            float range=CombatApproachRange(target,_encounter.ForceMelee);
            bool sight=CombatSight(target);
            if(me.GetExactDist(target)>range||!sight)
                return ApproachInRange(target,range,sight,"Getting into attack range");
            CommandPetAttack(target);
            // Auto Shot is a persistent auto-repeat, not a cast that should freeze
            // the combat routine. Cancel it before considering close-range attacks.
            if (HunterNeedsMelee(target) || me.IsWithinMeleeRange(target)) me.InterruptSpell(CurrentSpellTypes.AutoRepeat);
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true)) return SIBehaviorResult.Running;
            StandUp(); Dismount();
            FaceTarget(target);
            me.Attack(target, true);
            if (me.GetVictim() != target) return SIBehaviorResult.Failure;
            bool ranged = SICombatSpacing.RangedAutoAttack(ActiveClassStrategy().Melee, HunterNeedsMelee(target),
                me.GetItemByPos(EquipmentSlot.Ranged) != null, me.IsWithinMeleeRange(target));
            SetIsRangedAttacker(ranged);
            if (!ranged && me.GetClass() == Class.Hunter) me.InterruptSpell(CurrentSpellTypes.AutoRepeat);
            // Take an available close-range swing before starting another cast.
            // Starting a cast first could suppress this fallback on every tick.
            me.DoMeleeAttackIfReady();
            SIRoutineExecution.Fight(me.IsInCombat(), stage => TickRoutinePhase(stage, target));
            DoAutoAttackIfReady();
            return SIBehaviorResult.Running;
        }

        long _nextPetAttackCommand;
        ObjectGuid _petCommandTarget;
        void CommandPetAttack(Unit target)
        {
            if (target == null || !target.IsAlive() || me.GetPet()?.IsAlive() != true) return;
            if (_petCommandTarget == target.GetGUID() && Now < _nextPetAttackCommand) return;
            _petCommandTarget = target.GetGUID(); _nextPetAttackCommand = Now + 1000;
            me.GetSession().PetAttackForSI(target);
        }

        SIBehaviorResult TickRoutinePhase(SICombatActionStage stage, Unit target)
        {
            if (me.IsNonMeleeSpellCast(false, skipAutorepeat: true) || stage != SICombatActionStage.Reaction &&
                (Now < _nextRoutineAction || _lastRoutineSpell != null && me.GetSpellHistory().HasGlobalCooldown(_lastRoutineSpell)))
                return SIBehaviorResult.Running;
            // On trivial creatures try ready instant damage before the class
            // opener/rotation can commit to a long cast. The normal cast gate
            // still decides range, resources, cooldowns and target legality.
            if(target!=null&&(stage is SICombatActionStage.Pull or SICombatActionStage.Combat)&&
                SIInstantAttackPolicy.Prefer(me.GetLevel(),target.GetLevel(),target is Creature))
                foreach(var spell in OffensiveSpells(target).Where(SIInstantAttackPolicy.Instant))
                    if(Cast(spell,target))return SIBehaviorResult.Running;
            bool acted = stage switch
            {
                SICombatActionStage.Rest => Recuperate() || Rest(),
                SICombatActionStage.PreCombatBuff => Buff(),
                SICombatActionStage.Heal => TryClassAction(stage, target) || Survive(),
                _ => TryClassAction(stage, target)
            };
            if (acted) return SIBehaviorResult.Running;
            if (stage == SICombatActionStage.Combat)
                foreach (SpellInfo spell in OffensiveSpells(target))
                    if (Cast(spell, target)) return SIBehaviorResult.Running;
            return SIBehaviorResult.Failure;
        }
    }
}
