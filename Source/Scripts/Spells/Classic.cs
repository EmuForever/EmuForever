// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game;
using Game.AI;
using Game.DataStorage;
using Game.Entities;
using Game.Maps;
using Game.Scripting;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Linq;
using static Global;

// Classic 1.60 (WoW Forever) spell scripts, ported from the reference TrinityCore (src/server/scripts/Custom/Classic/world). The world
// database binds these names in spell_script_names; without a C# class of the same name the binding silently does nothing. The talent
// scripts are in ClassicMageShaman.cs, ClassicPriestPaladin.cs, ClassicWarlockDruid.cs and ClassicWarriorRogueHunter.cs.
// Not ported: the Legacy perk scripts (core: Player.Legacy.cs, LegacyDefinitions.cs) and classic_spell_profession_certification
// (needs the account_classic_certification table and the profession titles of the reference core).
namespace Scripts.Spells.Classic
{
    struct SpellIds
    {
        // Hunter
        public const int TameBeastTame = 13481;

        // Paladin
        public const int JudgementOfTheCrusaderRefreshProc = 25942;
        public const int LightsFury = 1310927;

        // Priest
        public const int VampiricEmbraceHeal = 15290;

        // Warlock
        public const int ImmolateConflagrateMarker = 1282590;

        // Warrior
        public const int GoreDrinkerTalent = 1323967;
        public const int GoreDrinkerBuff = 1323969;

        // Tauren
        public const int CultivateHerb = 1312643;
    }

    static class ClassicSpells
    {
        public static int GetSealJudgement(int sealSpellId) { return ClassicSeals.GetJudgement(sealSpellId); }

        // Base mana cost of a spell (flat cost plus the percentage of base mana), before cost modifiers
        public static int GetBaseManaCost(SpellInfo spellInfo, Unit caster)
        {
            foreach (SpellPowerRecord power in spellInfo.PowerCosts)
            {
                if (power == null || power.PowerType != PowerType.Mana)
                    continue;

                int cost = power.ManaCost;
                if (power.PowerCostPct > 0.0f)
                    cost += (int)MathFunctions.CalculatePct((float)caster.GetCreateMana(), power.PowerCostPct);
                return cost;
            }
            return 0;
        }

        public static bool IsComboPointBuilder(SpellInfo spellInfo)
        {
            if (spellInfo == null)
                return false;

            foreach (SpellEffectInfo spellEffectInfo in spellInfo.GetEffects())
                if (spellEffectInfo.IsEffect(SpellEffectName.Energize) && spellEffectInfo.MiscValue == (int)PowerType.ComboPoints)
                    return true;

            return false;
        }

        // Enemies inside the caster's area triggers of this spell (Blizzard, Flamestrike, Rain of Fire, Volley)
        public static List<Unit> GetGroundTargets(Unit caster, SpellInfo spellInfo)
        {
            HashSet<ObjectGuid> guids = new();
            foreach (AreaTrigger areaTrigger in caster.GetAreaTriggers(spellInfo.Id))
                guids.UnionWith(areaTrigger.GetInsideUnits());

            List<Unit> targets = new();
            foreach (ObjectGuid guid in guids)
            {
                Unit target = ObjAccessor.GetUnit(caster, guid);
                if (target != null && target.IsAlive() && caster.IsValidAttackTarget(target, spellInfo))
                    targets.Add(target);
            }
            return targets;
        }

        // Deals spell damage without a damage spell (periodic log), like a periodic damage aura tick
        public static void DealPeriodicSpellDamage(Unit caster, Unit target, SpellInfo spellInfo, Aura aura, int damage)
        {
            SpellNonMeleeDamage damageInfo = new(caster, target, spellInfo, aura.GetSpellVisual(), spellInfo.GetSchoolMask(), aura.GetCastId());
            damageInfo.periodicLog = true;
            caster.CalculateSpellDamageTaken(damageInfo, damage, spellInfo);
            Unit.DealDamageMods(damageInfo.attacker, damageInfo.target, ref damageInfo.damage, ref damageInfo.absorb);
            caster.SendSpellNonMeleeDamageLog(damageInfo);
            caster.DealSpellDamage(damageInfo, true);
        }

        public static bool IsFriendlyHolyTargetValid(Unit caster, Unit target, out SpellCastResult result)
        {
            result = SpellCastResult.SpellCastOk;
            if (target == null)
            {
                result = SpellCastResult.BadTargets;
                return false;
            }

            if (!caster.IsFriendlyTo(target))
            {
                if (!caster.IsValidAttackTarget(target))
                    result = SpellCastResult.BadTargets;
                else if (!caster.IsInFront(target))
                    result = SpellCastResult.UnitNotInfront;
            }
            return result == SpellCastResult.SpellCastOk;
        }
    }

    // Classic 1.60 ground spells (official client data): the spell creates an area trigger (effect 1) and puts a periodic dummy aura
    // on the caster (effect 2); every tick damages the enemies inside the caster's area trigger of that spell. The client data has
    // no damage spell or per-rank damage for them (placeholder values on effect 0): the tick damage per rank is the vanilla one,
    // the spell power share per tick the vanilla coefficient.
    [Script] // 10, 6141, 8427, 10185-10187 Blizzard; 2120, 2121, 8422, 8423, 10215, 10216 Flamestrike; 5740, 6219, 11677, 11678 Rain of Fire; 1510, 14294, 14295 Volley
    class classic_spell_ground_area_damage : AuraScript
    {
        static readonly Dictionary<int, (int TickDamage, float Coefficient)> GroundAreaDamages = new()
        {
            // Blizzard (frost, 1 s, 8 s channel): 33% spell power over 8 ticks
            { 10, (25, 0.0417f) }, { 6141, (44, 0.0417f) }, { 8427, (65, 0.0417f) },
            { 10185, (90, 0.0417f) }, { 10186, (117, 0.0417f) }, { 10187, (149, 0.0417f) },
            // Flamestrike burning ground (fire, 2 s, 8 s; the direct hit is effect 0): ~12% spell power over 4 ticks
            { 2120, (12, 0.0305f) }, { 2121, (22, 0.0305f) }, { 8422, (35, 0.0305f) },
            { 8423, (49, 0.0305f) }, { 10215, (66, 0.0305f) }, { 10216, (85, 0.0305f) },
            // Rain of Fire (fire, 2 s, 8 s channel): 33% spell power over 4 ticks
            { 5740, (42, 0.0833f) }, { 6219, (96, 0.0833f) }, { 11677, (155, 0.0833f) }, { 11678, (226, 0.0833f) },
            // Volley (arcane, 1 s, 6 s channel): no spell power
            { 1510, (50, 0.0f) }, { 14294, (65, 0.0f) }, { 14295, (80, 0.0f) },
        };

        public override bool Validate(SpellInfo spellInfo)
        {
            return GroundAreaDamages.ContainsKey(spellInfo.Id);
        }

        void HandleTick(AuraEffect aurEff)
        {
            Unit caster = GetCaster();
            if (caster == null)
                return;

            SpellInfo spellInfo = GetSpellInfo();
            var values = GroundAreaDamages[spellInfo.Id];

            foreach (Unit target in ClassicSpells.GetGroundTargets(caster, spellInfo))
            {
                int damage = values.TickDamage;
                if (values.Coefficient > 0.0f)
                    damage += (int)(caster.SpellBaseDamageBonusDone(spellInfo.GetSchoolMask()) * values.Coefficient);
                damage = target.SpellDamageBonusTaken(caster, spellInfo, damage, DamageEffectType.DOT);

                ClassicSpells.DealPeriodicSpellDamage(caster, target, spellInfo, GetAura(), damage);
            }
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            // channels (Blizzard, Rain of Fire, Volley): the ground effect ends with the channel
            if (GetSpellInfo().IsChanneled())
                GetCaster()?.RemoveAreaTrigger(GetSpellInfo().Id);
        }

        public override void Register()
        {
            OnEffectPeriodic.Add(new(HandleTick, 2, AuraType.PeriodicDummy));
            AfterEffectRemove.Add(new(HandleRemove, 2, AuraType.PeriodicDummy, AuraEffectHandleModes.Real));
        }
    }

    // Classic 1.60 (WoW Forever), Shen'dar Highlands cave, quest 94489 (official beta sniff 70205): clicking an injured druid makes the
    // player cast 1276047 (dummy) on it. Credit "Injured Druids healed" (257964) and the druid's own hidden objective (its entry).
    [Script]
    class classic_spell_heal_injured_druid : SpellScript
    {
        const int NpcInjuredDruidsHealedCredit = 257964;

        void HandleDummy(int effIndex)
        {
            Player player = GetCaster().ToPlayer();
            Creature druid = GetHitCreature();
            if (player == null || druid == null)
                return;

            player.KilledMonsterCredit(NpcInjuredDruidsHealedCredit);
            player.KilledMonsterCredit(druid.GetEntry());
            druid.SetStandState(UnitStandStateType.Stand);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // Classic 1.60 (WoW Forever) profession books: Mining for Dummies (247840, 1245608), Wild Harvest (247841, 1245609), Pelt Collecting for
    // Beginners (247846, 1245610). "Increases your <profession> skill by $m1. Cannot raise <profession> skill over $m2. You will learn
    // <profession> if it is not already trained and you do not already know two other professions." Both effects are dummies.
    [Script]
    class classic_spell_profession_book : SpellScript
    {
        static (SkillType Skill, int ApprenticeSpell)? GetProfession(int spellId)
        {
            switch (spellId)
            {
                case 1245608: return (SkillType.Mining, 2575);      // Mining (Apprentice)
                case 1245609: return (SkillType.Herbalism, 2366);   // Herb Gathering (Apprentice)
                case 1245610: return (SkillType.Skinning, 8613);    // Skinning (Apprentice)
                default: return null;
            }
        }

        int GetCap()
        {
            return Math.Max(GetSpellInfo().GetEffect(1).CalcValue(), 1);
        }

        SpellCastResult CheckCast()
        {
            Player player = GetCaster().ToPlayer();
            var book = GetProfession(GetSpellInfo().Id);
            if (player == null || !book.HasValue)
                return SpellCastResult.DontReport;

            if (player.HasSkill(book.Value.Skill))
                return player.GetPureSkillValue(book.Value.Skill) < GetCap() ? SpellCastResult.SpellCastOk : SpellCastResult.CantDoThatRightNow;

            return player.GetFreePrimaryProfessionPoints() > 0 ? SpellCastResult.SpellCastOk : SpellCastResult.CantDoThatRightNow;
        }

        void HandleDummy(int effIndex)
        {
            Player player = GetCaster().ToPlayer();
            var book = GetProfession(GetSpellInfo().Id);
            if (player == null || !book.HasValue)
                return;

            SkillType skill = book.Value.Skill;
            if (!player.HasSkill(skill))
                player.SpellBook.Learn(book.Value.ApprenticeSpell, false);
            if (!player.HasSkill(skill))
                return;

            int current = player.GetPureSkillValue(skill);
            int target = Math.Min(current + GetEffectValue(), GetCap());
            if (target > current)
                player.SetSkill(skill, player.GetSkillStep(skill), target, player.GetPureMaxSkillValue(skill));
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 20154, 21084, 20287-20293 - Seal of Righteousness: each melee swing that lands adds the rank's Holy damage spell. Vanilla formula on the
    // seal's points (which grow per level) and the weapon: slower weapons hit harder, two-handers more (sniffs of the official beta:
    // 20154 -> 25742, 21084 -> 25741, 3-6 damage at low level)
    [Script]
    class classic_spell_pal_seal_of_righteousness : AuraScript
    {
        static int GetDamageSpell(int sealId)
        {
            switch (sealId)
            {
                case 20154: return 25742;
                case 21084: return 25741;
                case 20287: return 25740;
                case 20288: return 25739;
                case 20289: return 25738;
                case 20290: return 25737;
                case 20291: return 25736;
                case 20292: return 25735;
                case 20293: return 25713;
                default: return 0;
            }
        }

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(GetDamageSpell(spellInfo.Id));
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            return GetTarget().IsPlayer() && eventInfo.GetActionTarget() != null && eventInfo.GetDamageInfo() != null
                && eventInfo.GetDamageInfo().GetAttackType() == WeaponAttackType.BaseAttack;
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            Player player = GetTarget().ToPlayer();
            Item weapon = player.GetWeaponForAttack(WeaponAttackType.BaseAttack, true);
            float speed = (weapon != null ? (int)weapon.GetTemplate().GetDelay() : SharedConst.BaseAttackTime) / 1000.0f;
            float points = aurEff.GetAmount();

            float damage;
            if (weapon != null && weapon.GetTemplate().GetInventoryType() == InventoryType.Weapon2Hand)
                damage = 1.2f * points * 1.2f * 1.03f * speed / 100.0f + 1.0f;
            else
                damage = 0.85f * MathF.Ceiling(points * 1.2f * 1.03f * speed / 100.0f) - 1.0f;
            damage += 0.03f * (player.GetWeaponDamageRange(WeaponAttackType.BaseAttack, WeaponDamageRange.MinDamage)
                + player.GetWeaponDamageRange(WeaponAttackType.BaseAttack, WeaponDamageRange.MaxDamage)) / 2.0f;

            // spell power is added by the damage spell itself (its bonus coefficient)
            player.CastSpell(eventInfo.GetActionTarget(), GetDamageSpell(GetId()), new CastSpellExtraArgs(aurEff)
                .AddSpellMod(SpellValueMod.BasePoint0, Math.Max((int)damage + 1, 1)));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 20271 - Judgement: unleashes the active seal's judgement on the target; the seal stays (sniff of the official beta 2026-10-06: Seal of
    // Righteousness -> 20187, Seal of the Crusader -> 21183, Seal of Righteousness keeps adding Holy damage to the swings after Judgement)
    [Script]
    class classic_spell_pal_judgement : SpellScript
    {
        public static bool IsCrusaderJudgement(int spellId)
        {
            switch (spellId)
            {
                case 21183: case 20188: case 20300: case 20301: case 20302: case 20303:
                    return true;
                default:
                    return false;
            }
        }

        void HandleScript(int effIndex)
        {
            Unit caster = GetCaster();
            Unit target = GetHitUnit();
            if (target == null)
                return;

            foreach (var (auraId, aurApp) in caster.GetAppliedAuras())
            {
                int judgement = ClassicSpells.GetSealJudgement(auraId);
                if (judgement == 0 || aurApp.GetBase().GetCasterGUID() != caster.GetGUID())
                    continue;

                caster.CastSpell(target, judgement, new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetTriggeringSpell(GetSpell()));
                // Judgement of the Crusader: the paladin's swings keep it on the target (25942 procs 25943, which casts it again)
                if (IsCrusaderJudgement(judgement))
                    caster.CastSpell(caster, SpellIds.JudgementOfTheCrusaderRefreshProc, new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetTriggeringSpell(GetSpell()));
                break;
            }
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleScript, 0, SpellEffectName.ScriptEffect));
        }
    }

    // 25943 - Judgement of the Crusader (refresh, from the 25942 swing proc): casts the paladin's Judgement of the Crusader on the target
    // again while it is on it (sniff 2026-10-06: every swing after the judgement shows 25942, 21183, 25943)
    [Script]
    class classic_spell_pal_judgement_of_the_crusader_refresh : SpellScript
    {
        void HandleDummy(int effIndex)
        {
            Unit caster = GetCaster();
            Unit target = GetHitUnit();
            if (target == null)
                return;

            foreach (var (auraId, aurApp) in target.GetAppliedAuras())
            {
                if (!classic_spell_pal_judgement.IsCrusaderJudgement(auraId) || aurApp.GetBase().GetCasterGUID() != caster.GetGUID())
                    continue;

                caster.CastSpell(target, auraId, new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetTriggeringSpell(GetSpell()));
                break;
            }
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 20473, 20929, 20930 - Holy Shock (trainer, levels 40/48/56): heals a friendly target or damages an enemy with the rank's spell
    // (the retail script only knows rank 1)
    [Script]
    class classic_spell_pal_holy_shock : SpellScript
    {
        static (int Damage, int Heal) GetRankSpells(int spellId)
        {
            switch (spellId)
            {
                case 20473: return (25912, 25914);
                case 20929: return (25911, 25913);
                case 20930: return (25902, 25903);
                default: return (0, 0);
            }
        }

        public override bool Validate(SpellInfo spellInfo)
        {
            var (damage, heal) = GetRankSpells(spellInfo.Id);
            return ValidateSpellInfo(damage, heal);
        }

        SpellCastResult CheckCast()
        {
            ClassicSpells.IsFriendlyHolyTargetValid(GetCaster(), GetExplTargetUnit(), out SpellCastResult result);
            return result;
        }

        void HandleDummy(int effIndex)
        {
            Unit caster = GetCaster();
            Unit target = GetHitUnit();
            if (target == null)
                return;

            var (damage, heal) = GetRankSpells(GetSpellInfo().Id);
            caster.CastSpell(target, caster.IsFriendlyTo(target) ? heal : damage, new CastSpellExtraArgs(GetSpell()));
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 1515 - Tame Beast: a 20 second channel in Classic (periodic aura on the beast); when it runs out the beast is tamed with 13481
    // (SPELL_EFFECT_TAME_CREATURE). The retail script on 1515 (spell_hun_tame_beast) keeps its cast checks; it tames instantly in
    // retail, so the Classic channel ended without a pet.
    [Script]
    class classic_spell_hun_tame_beast_channel : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.TameBeastTame);
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            if (GetTargetApplication().GetRemoveMode() != AuraRemoveMode.Expire)
                return;

            Unit caster = GetCaster();
            if (caster != null)
                caster.CastSpell(GetTarget(), SpellIds.TameBeastTame, new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetOriginalCaster(caster.GetGUID()));
        }

        public override void Register()
        {
            AfterEffectRemove.Add(new(HandleRemove, 1, AuraType.PeriodicTriggerSpell, AuraEffectHandleModes.Real));
        }
    }

    // 1280003, 1280046, 1271103 - Taming Rod (Skyborne hunter quests Taming the Beast 94978, 94979, 94013), and 1277794, 1278028,
    // 1278029 (human hunter quests 94792, 94863, 94864): a 20 second channel with a dummy aura on the beast; when it runs out the
    // rod's tame spell charms the beast for 12 sec and completes the quest (official beta sniff: channel 20000 ms, then the tame spell)
    [Script]
    class classic_spell_hun_taming_rod : AuraScript
    {
        static int GetTameSpell(int channelSpellId)
        {
            switch (channelSpellId)
            {
                case 1280003: return 1280004;   // Windsong Crawler (94978)
                case 1280046: return 1280044;   // Ornery Galestrider (94979)
                case 1271103: return 1271102;   // Vuldren Alpha (94013)
                case 1277794: return 1277851;   // Rockhide Boar (94792, human)
                case 1278028: return 1278060;   // Gray Forest Wolf (94863, human)
                case 1278029: return 1278061;   // Young Forest Bear (94864, human)
                default: return 0;
            }
        }

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(GetTameSpell(spellInfo.Id));
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            // act once, on the first aura effect, whatever its index and type in this build's data
            if (aurEff.GetEffIndex() != GetSpellInfo().GetEffects().First(effect => effect.IsAura()).EffectIndex)
                return;

            if (GetTargetApplication().GetRemoveMode() != AuraRemoveMode.Expire)
                return;

            Unit caster = GetCaster();
            if (caster != null && caster.IsAlive() && GetTarget().IsAlive())
                caster.CastSpell(GetTarget(), GetTameSpell(GetId()), new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetOriginalCaster(caster.GetGUID()));
        }

        public override void Register()
        {
            AfterEffectRemove.Add(new(HandleRemove, SpellConst.EffectAll, AuraType.Any, AuraEffectHandleModes.Real));
        }
    }

    // 348, 707, 1094, 2941, 11665, 11667, 11668, 25309 - Immolate: Classic 1.60 added a script effect (EFFECT_2) that puts the hidden
    // Immolate aura 1282590 on the target (sniff: both auras on the target, same caster). Every Conflagrate rank needs it
    // (SpellAuraRestrictions TargetAuraSpell 1282590), so without it Conflagrate could never be cast.
    [Script]
    class classic_spell_warl_immolate : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ImmolateConflagrateMarker) && ValidateSpellEffect((spellInfo.Id, 2));
        }

        void HandleScript(int effIndex)
        {
            // the marker targets its caster, so the target casts it on itself for the warlock
            Unit target = GetHitUnit();
            target.CastSpell(target, SpellIds.ImmolateConflagrateMarker, new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetOriginalCaster(GetCaster().GetGUID()));
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleScript, 2, SpellEffectName.ScriptEffect));
        }
    }

    // 1319421 - Motivated (Executor's Motivator, undead quest Discipline 99134): a dummy aura on a Tirisfal Deathguard. The official
    // server credits the objective (Deathguard Cyrus 1746, 5 needed) for any of them, with an empty victim GUID (sniff of 2026-10-06:
    // QUEST_UPDATE_ADD_CREDIT 99134 / 1746, 1..5 of 5; Motivated was seen on 1496, 1652, 1735, 1738, 1742-1746, 2210, 5725, 272101).
    [Script]
    class classic_spell_executors_motivator : AuraScript
    {
        const int QuestDiscipline = 99134;
        const int NpcDeathguardCyrus = 1746;

        static bool IsTirisfalDeathguard(int entry)
        {
            switch (entry)
            {
                case 1495: case 1496: case 1519: case 1652: case 1735: case 1736: case 1737: case 1738: case 1739: case 1740:
                case 1741: case 1742: case 1743: case 1744: case 1745: case 1746: case 2209: case 2210: case 5725:
                case 251001: case 257655: case 272101: case 275103:
                    return true;
                default:
                    return false;
            }
        }

        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Player player = GetCaster()?.ToPlayer();
            if (player == null || !IsTirisfalDeathguard(GetTarget().GetEntry()) || player.GetQuestStatus(QuestDiscipline) != QuestStatus.Incomplete)
                return;

            player.UpdateQuestObjectiveProgress(QuestObjectiveType.Monster, NpcDeathguardCyrus, 1, ObjectGuid.Empty, null,
                objective => objective.QuestID == QuestDiscipline);
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
        }
    }

    // Seal of Fury (Forever, 1311649 1311656 20163 20419 20421 20422 20423): "Fills the Paladin with divine fury for 30 sec, causing melee
    // attacks to deal an additional N Holy damage. While a shield is equipped, each attack also grants an absorb shield equal to 50% of the
    // Holy damage dealt." Its judgement (Judgement of Fury) is in Game.Spells.ClassicSeals.
    static class ClassicSealOfFury
    {
        public static readonly int[] Seals = { 1311649, 1311656, 20163, 20419, 20421, 20422, 20423 };

        public static int GetDamageSpell(int sealId)
        {
            switch (sealId)
            {
                case 1311649: return 1311647;
                case 1311656: return 1311654;
                case 20163: return 20231;
                case 20419: return 20415;
                case 20421: return 20416;
                case 20422: return 20417;
                case 20423: return 20418;
                default: return 0;
            }
        }
    }

    [Script]
    class classic_spell_pal_seal_of_fury : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(ClassicSealOfFury.GetDamageSpell(spellInfo.Id));
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            return eventInfo.GetActionTarget() != null && eventInfo.GetDamageInfo() != null
                && eventInfo.GetDamageInfo().GetAttackType() == WeaponAttackType.BaseAttack && eventInfo.GetSpellInfo() == null;
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            GetTarget().CastSpell(eventInfo.GetActionTarget(), ClassicSealOfFury.GetDamageSpell(GetId()), new CastSpellExtraArgs(aurEff));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // Seal of Fury Holy damage (1311647 1311654 20231 20415-20418): with a shield equipped the paladin gains Light's Fury (1310927), an
    // absorb shield of the seal's effect 1 percent (50) of the damage dealt
    [Script]
    class classic_spell_pal_seal_of_fury_damage : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.LightsFury);
        }

        void HandleAfterHit()
        {
            Player paladin = GetCaster().ToPlayer();
            if (paladin == null || GetHitDamage() <= 0)
                return;

            Item offhand = paladin.GetItemByPos(EquipmentSlot.OffHand);
            if (offhand == null || offhand.GetTemplate().GetClass() != ItemClass.Armor || offhand.GetTemplate().GetSubClass() != (int)ItemSubClassArmor.Shield)
                return;

            foreach (int sealId in ClassicSealOfFury.Seals)
            {
                Aura seal = paladin.GetAura(sealId);
                if (seal == null)
                    continue;

                // effect 1 is a plain value (Effect 0), not an aura effect
                int absorb = MathFunctions.CalculatePct(GetHitDamage(), seal.GetSpellInfo().GetEffect(1).CalcValue(paladin));
                if (absorb > 0)
                    paladin.CastSpell(paladin, SpellIds.LightsFury, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.BasePoint0, absorb));
                break;
            }
        }

        public override void Register()
        {
            AfterHit.Add(new(HandleAfterHit));
        }
    }

    // Judgement of Fury (20183 20411-20414 1311650 1311655): "Unleashing this Seal's energy causes Holy damage to an enemy and taunts the
    // target to attack you for 4 sec." Effect 1 (dummy) is the taunt, effect 2 (dummy) the extra threat.
    [Script]
    class classic_spell_pal_judgement_of_fury : SpellScript
    {
        void HandleTaunt(int effIndex)
        {
            Creature target = GetHitUnit()?.ToCreature();
            Unit paladin = GetCaster().ToUnit();
            if (target == null || paladin == null || !target.CanHaveThreatList())
                return;

            target.GetThreatManager().MatchUnitThreatToHighestThreat(paladin);
            if (target.IsAIEnabled())
                target.GetAI().AttackStart(paladin);
        }

        // effect 2 has no implicit target, so it runs once in hit mode against the judged unit
        void HandleThreat(int effIndex)
        {
            Creature target = GetExplTargetUnit()?.ToCreature();
            Unit paladin = GetCaster().ToUnit();
            if (target != null && paladin != null && target.CanHaveThreatList())
                target.GetThreatManager().AddThreat(paladin, GetEffectValue(), GetSpellInfo(), true);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleTaunt, 1, SpellEffectName.Dummy));
            OnEffectHit.Add(new(HandleThreat, 2, SpellEffectName.Dummy));
        }
    }

    // 26573, 20116, 20922, 20923, 20924 - Consecration (Classic 1.60, official dungeon sniff): the cast puts a 1 sec periodic dummy on the
    // paladin for 8 sec and creates area trigger 41311-41316 at his feet (no data on our server); each tick the official server casts the
    // rank's Consecration damage (1280345-1280349) at the consecrated spot, hitting the enemies standing in it.
    [Script]
    class classic_spell_pal_consecration : AuraScript
    {
        Position _center;

        static int TickSpell(int spellId)
        {
            switch (spellId)
            {
                case 26573: return 1280345;
                case 20116: return 1280346;
                case 20922: return 1280347;
                case 20923: return 1280348;
                case 20924: return 1280349;
                default: return 0;
            }
        }

        public override bool Validate(SpellInfo spellInfo)
        {
            return TickSpell(spellInfo.Id) != 0 && ValidateSpellInfo(TickSpell(spellInfo.Id));
        }

        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            _center = GetUnitOwner().GetPosition();
        }

        void HandleTick(AuraEffect aurEff)
        {
            PreventDefaultAction();
            Unit paladin = GetUnitOwner();
            paladin.CastSpell(_center ?? paladin.GetPosition(), TickSpell(GetId()), new CastSpellExtraArgs(aurEff));
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 2, AuraType.PeriodicDummy, AuraEffectHandleModes.Real));
            OnEffectPeriodic.Add(new(HandleTick, 2, AuraType.PeriodicDummy));
        }
    }

    // 15286 - Vampiric Embrace (vanilla, VMaNGOS UnitAuraProcHandler): a debuff on the enemy. Shadow damage the priest deals to it
    // heals the priest's party (15290, the priest included) for 20% of the damage. The retail script treated it as a self buff and
    // made the debuffed enemy cast the heal.
    [Script]
    class classic_spell_pri_vampiric_embrace : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.VampiricEmbraceHeal);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            return damageInfo != null && damageInfo.GetDamage() != 0 && eventInfo.GetActor() != null && eventInfo.GetActor().GetGUID() == GetCasterGUID();
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            Unit priest = eventInfo.GetActor();
            int heal = Math.Max(1, MathFunctions.CalculatePct(eventInfo.GetDamageInfo().GetDamage(), aurEff.GetAmount()));
            priest.CastSpell(priest, SpellIds.VampiricEmbraceHeal, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, heal));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 5217 - Tiger's Fury (Classic 1.60): a dummy aura, effect 0 = 15, "increases physical damage done by 15% for 6 sec". Applied to melee
    // swings and physical abilities; the periodic hook carries no school, so bleeds are left alone.
    [Script]
    class classic_unit_tigers_fury : UnitScript
    {
        const int SpellTigersFury = 5217;

        public classic_unit_tigers_fury() : base("classic_unit_tigers_fury") { }

        static void Apply(Unit attacker, ref int damage)
        {
            if (attacker == null || damage == 0 || attacker.GetClass() != Class.Druid)
                return;

            AuraEffect fury = attacker.GetAuraEffect(SpellTigersFury, 0);
            if (fury != null)
                MathFunctions.AddPct(ref damage, fury.GetAmount());
        }

        public override void ModifyMeleeDamage(Unit target, Unit attacker, ref int damage)
        {
            Apply(attacker, ref damage);
        }

        public override void ModifySpellDamageTaken(Unit target, Unit attacker, ref int damage, SpellInfo spellInfo)
        {
            if (spellInfo == null || damage <= 0 || !spellInfo.GetSchoolMask().HasAnyFlag(SpellSchoolMask.Normal))
                return;

            Apply(attacker, ref damage);
        }
    }

    // Tauren racial Cultivation (1.60): "Cultivate a nearby herb, growing a duplicate you can harvest without requiring Herbalism skill.
    // Each herb may only be cultivated once." Official sniffs (2026-09-28, 2026-10-01): the spellbook spell 20552 (instant, dummy on game
    // objects within 5 yd) makes the server start 1312643 (3 sec cast) on the herb; when it completes a duplicate herb appears on the same
    // spot, created by the player: same model and loot, lock 4522 = key spell 1312650 (open lock without skill). A twin is found by model + loot.
    static class ClassicCultivation
    {
        const int LockCultivatedHerb = 4522;
        const float SearchRange = 5.0f;
        public static readonly TimeSpan TwinDuration = TimeSpan.FromMinutes(5);
        static readonly TimeSpan HerbCooldown = TimeSpan.FromMinutes(15);   // "each herb may only be cultivated once" (until it has been picked and regrown)

        static Dictionary<(int DisplayId, int Loot), int> _twins;
        static readonly Dictionary<ObjectGuid, DateTime> CultivatedHerbs = new();
        static readonly object CultivatedHerbsLock = new();

        static bool IsHerbalismLock(int lockId)
        {
            LockRecord lockRecord = CliDB.LockStorage.LookupByKey(lockId);
            if (lockRecord == null)
                return false;
            for (int i = 0; i < SharedConst.MaxLockCase; ++i)
                if (lockRecord.Type[i] == (byte)LockKeyType.Skill && lockRecord.Index[i] == (int)LockType.Herbalism)
                    return true;
            return false;
        }

        // herb (model, loot) -> twin entry
        public static int GetTwinEntry(GameObjectTemplate herb)
        {
            if (_twins == null)
            {
                Dictionary<(int, int), int> twins = new();
                foreach (var (entry, goInfo) in ObjectMgr.GetGameObjectTemplates())
                    if (goInfo.type == GameObjectTypes.Chest && goInfo.Chest.open == LockCultivatedHerb)
                        twins.TryAdd((goInfo.displayId, goInfo.Chest.chestLoot), entry);
                _twins = twins;
            }

            if (herb == null || herb.type != GameObjectTypes.Chest || !IsHerbalismLock(herb.Chest.open))
                return 0;
            return _twins.LookupByKey((herb.displayId, herb.Chest.chestLoot));
        }

        static bool WasCultivated(ObjectGuid herb)
        {
            lock (CultivatedHerbsLock)
            {
                if (!CultivatedHerbs.TryGetValue(herb, out DateTime when))
                    return false;
                if (DateTime.UtcNow - when < HerbCooldown)
                    return true;
                CultivatedHerbs.Remove(herb);
                return false;
            }
        }

        public static void MarkCultivated(ObjectGuid herb)
        {
            lock (CultivatedHerbsLock)
                CultivatedHerbs[herb] = DateTime.UtcNow;
        }

        public static bool CanCultivate(GameObject go)
        {
            return go != null && go.IsSpawned() && go.GetGoState() == GameObjectState.Ready && go.GetOwnerGUID().IsEmpty()
                && GetTwinEntry(go.GetGoInfo()) != 0 && !WasCultivated(go.GetGUID());
        }

        public static GameObject FindHerb(Unit caster)
        {
            return caster.GetGameObjectListWithEntryInGrid(0, SearchRange)
                .Where(CanCultivate)
                .OrderBy(go => caster.GetExactDist2dSq(go))
                .FirstOrDefault();
        }
    }

    // 20552 - Cultivation (Tauren racial, spellbook): starts Cultivate (1312643) on the nearest herb within 5 yd
    [Script]
    class classic_spell_tauren_cultivation : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.CultivateHerb);
        }

        SpellCastResult CheckCast()
        {
            return ClassicCultivation.FindHerb(GetCaster().ToUnit()) != null ? SpellCastResult.SpellCastOk : SpellCastResult.NoValidTargets;
        }

        void HandleAfterCast()
        {
            GameObject herb = ClassicCultivation.FindHerb(GetCaster().ToUnit());
            if (herb != null)
                GetCaster().CastSpell(herb, SpellIds.CultivateHerb, new CastSpellExtraArgs(TriggerCastFlags.IgnoreGCD));
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            AfterCast.Add(new(HandleAfterCast));
        }
    }

    // 1312643 - Cultivate: the herb's twin grows on the same spot, for the caster only
    [Script]
    class classic_spell_tauren_cultivate_herb : SpellScript
    {
        SpellCastResult CheckCast()
        {
            return ClassicCultivation.CanCultivate(GetExplTargetGObj()) ? SpellCastResult.SpellCastOk : SpellCastResult.BadTargets;
        }

        void HandleDummy(int effIndex)
        {
            Player caster = GetCaster().ToPlayer();
            GameObject herb = GetHitGObj();
            if (caster == null || !ClassicCultivation.CanCultivate(herb))
                return;

            int twinEntry = ClassicCultivation.GetTwinEntry(herb.GetGoInfo());
            GameObject twin = GameObject.CreateGameObject(twinEntry, herb.GetMap(), herb, herb.GetLocalRotation(), 255, GameObjectState.Ready);
            if (twin == null)
                return;

            ClassicCultivation.MarkCultivated(herb.GetGUID());
            PhasingHandler.InheritPhaseShift(twin, caster);
            twin.SetPrivateObjectOwner(caster.GetGUID());
            twin.SetRespawnTime(ClassicCultivation.TwinDuration);
            caster.AddGameObject(twin);
            herb.GetMap().AddToMap(twin);
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // Classic 1.60 (client 70374) warrior talent Gore Drinker 1323967: "Your Enrage, Berserker Rage, Bloodrage, Death Wish, and Bloodthirst
    // abilities cause your next $m1 melee attacks to restore ${$s2}.1% of your maximum Health." (both effects are server-side dummies: 4
    // attacks, 0.5 / 1% by rank). The buff 1323969 procs on melee attacks (SPELL_AURA_PROC_TRIGGER_SPELL_WITH_VALUE) and heals with 1323968
    // for its amount.
    static class ClassicGoreDrinker
    {
        public static void Apply(Unit warrior)
        {
            AuraEffect attacks = warrior.GetAuraEffect(SpellIds.GoreDrinkerTalent, 0);
            AuraEffect healthPct = warrior.GetAuraEffect(SpellIds.GoreDrinkerTalent, 1);
            if (attacks == null || healthPct == null || attacks.GetAmount() <= 0 || healthPct.GetAmount() <= 0)
                return;

            int heal = Math.Max(1, (int)warrior.CountPctFromMaxHealth(healthPct.GetAmount()));
            warrior.CastSpell(warrior, SpellIds.GoreDrinkerBuff, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.BasePoint0, heal));
            warrior.GetAura(SpellIds.GoreDrinkerBuff)?.SetCharges((byte)attacks.GetAmount());
        }
    }

    // 2687 - Bloodrage, 18499 - Berserker Rage, 12328 - Death Wish, 23881, 23892, 23893, 23894 - Bloodthirst: Gore Drinker
    [Script]
    class classic_spell_warr_gore_drinker : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.GoreDrinkerBuff);
        }

        void HandleAfterCast()
        {
            ClassicGoreDrinker.Apply(GetCaster().ToUnit());
        }

        public override void Register()
        {
            AfterCast.Add(new(HandleAfterCast));
        }
    }

    // 12880 - Enrage (talent proc buff): Gore Drinker
    [Script]
    class classic_spell_warr_gore_drinker_enrage : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.GoreDrinkerBuff);
        }

        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            ClassicGoreDrinker.Apply(GetTarget());
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 0, AuraType.Any, AuraEffectHandleModes.Real));
        }
    }
}
