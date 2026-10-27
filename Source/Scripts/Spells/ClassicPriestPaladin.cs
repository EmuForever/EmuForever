// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Dynamic;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Linq;
using static Global;

// Classic 1.60 (WoW Forever) priest and paladin talents, ported from the reference TrinityCore
// (src/server/scripts/Custom/Classic/world/talents/classic_talents_priest_paladin.cpp). In the client data these talents are dummy auras
// (or dummy spells) with no server side behaviour; the scripts implement the Forever tooltips with the helper spells found in the 1.60
// data. Talent rank values come from the trait curves, so every amount is read from the talent aura effect, never hardcoded.
namespace Scripts.Spells.Classic.PriestPaladin
{
    struct SpellIds
    {
        // Priest
        public const int InspirationBuff = 14893;
        public const int BlessedRecoveryHot = 27813;
        public const int BlessedRecoveryHeal = 1240755;
        public const int PrayerOfMendingBounce = 401882;
        public const int RenewedHope = 425280;
        public const int WeakenedSoul = 6788;
        public const int DivineAegisAbsorb = 431624;
        public const int PowerInLight = 1309969;
        public const int EarlyDemise = 1310076;
        public const int LitanyOfLightEnergize = 1317007;

        // Paladin
        public const int EyeForAnEyeDamage = 25997;
        public const int RedoubtBuff = 20128;
        public const int InfusionOfLightBuff = 437063;
        public const int ConsecratedGround = 1310905;
        public const int LightsVigilEnergize = 1311601;
        public const int SanctifiedJudgement = 1311074;
        public const int SanctifiedJudgementEnergize = 1311077;
        public const int ImprovedSealOfFury = 1314103;
        public const int ImprovedSealOfFuryEnergize = 1314104;
        public const int SacredShieldAbsorb = 412018;
        public const int SacredShieldFlashOfLight = 412020;
        public const int HolyShockForeverDamage = 1311604;
        public const int HolyShockForeverHeal = 1311605;
    }

    /*######
    ## Priest
    ######*/

    // 14892 - Inspiration: "Your non-periodic critical heals increase your target's Armor by X% for 15 sec." Critical direct heals cast
    // 14893 (armor percent) on the healed target with the talent amount. Crit only + triggered heals (Penance, Holy Shock) via spell_proc.
    [Script]
    class classic_spell_pri_inspiration : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.InspirationBuff);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            HealInfo healInfo = eventInfo.GetHealInfo();
            return healInfo != null && healInfo.GetHeal() != 0 && eventInfo.GetActionTarget() != null && eventInfo.GetHitMask().HasAnyFlag(ProcFlagsHit.Critical);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            CastSpellExtraArgs args = new(aurEff);
            if (aurEff.GetAmount() > 0)
                args.AddSpellMod(SpellValueMod.BasePoint0, aurEff.GetAmount());
            GetTarget().CastSpell(eventInfo.GetActionTarget(), SpellIds.InspirationBuff, args);
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 27811 - Blessed Recovery: "After being struck by a melee or ranged critical hit, or suffering more than 30% of your maximum Health from a
    // single attack, heal 8% of the damage taken over 6 sec. Refreshing this effect carries over any remaining healing." Effect 0 is the heal
    // percentage, effect 1 the health threshold. The heal is 27813 (1.60: periodic dummy, 3 ticks) whose amount holds the total; the remaining
    // part of a running one is added on refresh.
    [Script]
    class classic_spell_pri_blessed_recovery : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.BlessedRecoveryHot) && ValidateSpellEffect((spellInfo.Id, 1));
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            if (damageInfo == null || damageInfo.GetDamage() == 0 || !GetTarget().IsAlive())
                return false;

            if (eventInfo.GetHitMask().HasAnyFlag(ProcFlagsHit.Critical))
                return true;

            AuraEffect threshold = GetEffect(1);
            return threshold != null && damageInfo.GetDamage() > GetTarget().CountPctFromMaxHealth(threshold.GetAmount());
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            Unit target = GetTarget();
            int heal = MathFunctions.CalculatePct(eventInfo.GetDamageInfo().GetDamage(), aurEff.GetAmount());

            Aura running = target.GetAura(SpellIds.BlessedRecoveryHot, target.GetGUID());
            if (running != null)
            {
                AuraEffect runningEff = running.GetEffect(0);
                if (runningEff != null && runningEff.GetTotalTicks() != 0)
                    heal += (int)((long)runningEff.GetAmount() * runningEff.GetRemainingTicks() / runningEff.GetTotalTicks());
                running.Remove();
            }

            if (heal > 0)
                target.CastSpell(target, SpellIds.BlessedRecoveryHot, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, heal));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 27813 - Blessed Recovery (periodic dummy, 2 sec, 6 sec): each tick heals an equal share of the total (1240755)
    [Script]
    class classic_spell_pri_blessed_recovery_hot : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.BlessedRecoveryHeal);
        }

        void HandleTick(AuraEffect aurEff)
        {
            int totalTicks = aurEff.GetTotalTicks();
            if (totalTicks == 0)
                return;

            int heal = aurEff.GetAmount() / totalTicks;
            if (heal > 0)
                GetTarget().CastSpell(GetTarget(), SpellIds.BlessedRecoveryHeal, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, heal));
        }

        public override void Register()
        {
            OnEffectPeriodic.Add(new(HandleTick, 0, AuraType.PeriodicDummy));
        }
    }

    // 401859, 1240826, 1240827 - Prayer of Mending (ranks 1-3): "Places a spell on the target that heals them the next time they take damage
    // or receive non-periodic healing. When the heal occurs, Prayer of Mending jumps to a party or raid member within 20 yards. Jumps up to
    // 5 times and lasts 30 sec after each jump." Cast: effect 0 holds the heal (base + 42.9% healing power), effect 1 the charges.
    static class ClassicPrayerOfMending
    {
        public record Rank(int Cast, int Aura, int Heal, int Jump);   // Aura: 30 sec, dummy, stacks = charges; Jump: dummy, ally area 20 yd
        public record JumpArgs(int Heal, int Charges);

        static readonly Rank[] Ranks =
        {
            new(401859, 401877, 401863, 401880),
            new(1240826, 1240848, 1313379, 1240918),
            new(1240827, 1240849, 1313381, 1240919),
        };

        public static Rank GetRank(int spellId)
        {
            return Ranks.FirstOrDefault(rank => rank.Cast == spellId || rank.Aura == spellId || rank.Heal == spellId || rank.Jump == spellId);
        }

        public static void CastAura(Unit caster, Unit target, int auraId, int heal, int charges)
        {
            CastSpellExtraArgs args = new(TriggerCastFlags.FullMask);
            args.AddSpellMod(SpellValueMod.BasePoint0, heal);
            args.AddSpellMod(SpellValueMod.AuraStack, Math.Max(charges, 1));
            caster.CastSpell(target, auraId, args);
        }
    }

    [Script]
    class classic_spell_pri_prayer_of_mending : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            var rank = ClassicPrayerOfMending.GetRank(spellInfo.Id);
            return rank != null && ValidateSpellInfo(rank.Aura, rank.Heal, rank.Jump) && ValidateSpellEffect((spellInfo.Id, 1));
        }

        void HandleDummy(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            Unit target = GetHitUnit();
            if (target == null)
                return;

            SpellEffectInfo healEffect = GetEffectInfo();
            int heal = healEffect.CalcValue(caster) + (int)(caster.SpellBaseHealingBonusDone(GetSpellInfo().GetSchoolMask()) * healEffect.BonusCoefficient);
            int charges = GetSpellInfo().GetEffect(1).CalcValue(caster);

            ClassicPrayerOfMending.CastAura(caster, target, ClassicPrayerOfMending.GetRank(GetSpellInfo().Id).Aura, heal, charges);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 401877, 1240848, 1240849 - Prayer of Mending (aura): damage taken or a direct heal received heals the holder with the stored amount,
    // then the holder casts the jump spell carrying the heal and the remaining charges
    [Script]
    class classic_spell_pri_prayer_of_mending_aura : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            var rank = ClassicPrayerOfMending.GetRank(spellInfo.Id);
            return rank != null && ValidateSpellInfo(rank.Heal, rank.Jump);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            if (!GetTarget().IsAlive())
                return false;
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            if (damageInfo != null)
                return damageInfo.GetDamage() > 0;
            HealInfo healInfo = eventInfo.GetHealInfo();
            return healInfo != null && healInfo.GetHeal() > 0;
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            var rank = ClassicPrayerOfMending.GetRank(GetId());
            Unit holder = GetTarget();
            Unit caster = GetCaster();
            ObjectGuid casterGuid = GetCasterGUID();
            int heal = aurEff.GetAmount();
            int charges = GetStackAmount();

            // removed first: the heal below must not consume a charge of a new application
            Remove();

            if (caster == null)
                return;

            caster.CastSpell(holder, rank.Heal, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.BasePoint0, heal));

            if (charges > 1)
                holder.CastSpell(holder, rank.Jump, new CastSpellExtraArgs(TriggerCastFlags.FullMask)
                    .SetOriginalCaster(casterGuid)
                    .SetCustomArg(new ClassicPrayerOfMending.JumpArgs(heal, charges - 1)));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 401880, 1240918, 1240919 - Prayer of Mending (jump): picks one injured party or raid member of the holder within 20 yards (not the
    // holder) and places Prayer of Mending on it for the original priest with the remaining charges
    [Script]
    class classic_spell_pri_prayer_of_mending_jump : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            var rank = ClassicPrayerOfMending.GetRank(spellInfo.Id);
            return rank != null && ValidateSpellInfo(rank.Aura, SpellIds.PrayerOfMendingBounce);
        }

        void FilterTargets(List<WorldObject> targets)
        {
            Unit holder = GetCaster().ToUnit();
            targets.RemoveAll(obj =>
            {
                Unit unit = obj.ToUnit();
                return unit == null || unit == holder || !unit.IsAlive() || !holder.IsInRaidWith(unit);
            });
            SelectRandomInjuredTargets(targets, 1, true);
        }

        void HandleJump(int effIndex)
        {
            Unit priest = GetOriginalCaster();
            Unit target = GetHitUnit();
            if (priest == null || target == null || GetSpell().m_customArg is not ClassicPrayerOfMending.JumpArgs jump)
                return;

            ClassicPrayerOfMending.CastAura(priest, target, ClassicPrayerOfMending.GetRank(GetSpellInfo().Id).Aura, jump.Heal, jump.Charges);
            GetCaster().CastSpell(target, SpellIds.PrayerOfMendingBounce, new CastSpellExtraArgs(TriggerCastFlags.FullMask));
        }

        public override void Register()
        {
            OnObjectAreaTargetSelect.Add(new(FilterTargets, 0, Targets.UnitSrcAreaAlly));
            OnEffectHitTarget.Add(new(HandleJump, 0, SpellEffectName.Dummy));
        }
    }

    // 402174, 1240720, 1240721, 1316995 - Penance (ranks 1-4): "Launches a volley of holy light at the target, causing Holy damage to an enemy,
    // or healing to an ally, instantly and every 1 sec for 2 sec." The dummy starts the rank's 2 sec channel (enemy or ally version); the
    // channel aura triggers the damage or heal spell at once and every second (1.60 data).
    [Script]
    class classic_spell_pri_penance : SpellScript
    {
        static (int Damage, int Heal) GetChannelSpells(int spellId)
        {
            switch (spellId)
            {
                case 402174: return (402261, 402277);
                case 1240720: return (1240734, 1240732);
                case 1240721: return (1240736, 1240733);
                case 1316995: return (1316994, 1316992);
                default: return (0, 0);
            }
        }

        public override bool Validate(SpellInfo spellInfo)
        {
            var (damage, heal) = GetChannelSpells(spellInfo.Id);
            return ValidateSpellInfo(damage, heal);
        }

        SpellCastResult CheckCast()
        {
            ClassicSpells.IsFriendlyHolyTargetValid(GetCaster().ToUnit(), GetExplTargetUnit(), out SpellCastResult result);
            return result;
        }

        void HandleDummy(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            Unit target = GetHitUnit();
            if (target == null)
                return;

            var (damage, heal) = GetChannelSpells(GetSpellInfo().Id);
            caster.CastSpell(target, caster.IsFriendlyTo(target) ? heal : damage,
                new CastSpellExtraArgs(TriggerCastFlags.IgnoreGCD | TriggerCastFlags.IgnoreSpellAndCategoryCD).SetTriggeringSpell(GetSpell()));
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 425280 - Renewed Hope: "Your heals from Flash Heal, Binding Heal, Lesser Heal, Heal, Greater Heal, and Penance gain 10% increased critical
    // strike chance when cast on targets with Weakened Soul, and reduce the remaining duration of Weakened Soul on their target by 5 sec."
    // The talent aura (proc limited to those heals by spell_proc) shortens Weakened Soul by effect 1 (-5000 ms) ...
    [Script]
    class classic_spell_pri_renewed_hope : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.WeakenedSoul) && ValidateSpellEffect((spellInfo.Id, 1));
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            Unit target = eventInfo.GetActionTarget();
            AuraEffect reduction = GetEffect(1);
            if (target == null || reduction == null)
                return;

            Aura weakenedSoul = target.GetAura(SpellIds.WeakenedSoul);
            if (weakenedSoul == null)
                return;

            int duration = weakenedSoul.GetDuration() + reduction.GetAmount();
            if (duration <= 0)
                weakenedSoul.Remove();
            else
                weakenedSoul.SetDuration((Milliseconds)duration);
        }

        public override void Register()
        {
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // ... and the heals themselves get the crit bonus (effect 0) on targets with Weakened Soul: Flash Heal, Binding Heal, Lesser Heal, Heal,
    // Greater Heal (all ranks) and the Penance heal spells
    [Script]
    class classic_spell_pri_renewed_hope_crit : SpellScript
    {
        void CalcCritChance(Unit victim, ref float critChance)
        {
            if (victim == null || !victim.HasAura(SpellIds.WeakenedSoul))
                return;

            AuraEffect renewedHope = GetCaster().ToUnit()?.GetAuraEffect(SpellIds.RenewedHope, 0);
            if (renewedHope != null)
                critChance += renewedHope.GetAmount();
        }

        public override void Register()
        {
            OnCalcCritChance.Add(new(CalcCritChance));
        }
    }

    // 431622 - Divine Aegis: "Your critical heals create a protective shield on the target, absorbing 15% of the amount healed. Lasts 12 sec."
    // (431624, crit only + triggered heals via spell_proc)
    [Script]
    class classic_spell_pri_divine_aegis : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.DivineAegisAbsorb);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            HealInfo healInfo = eventInfo.GetHealInfo();
            return healInfo != null && healInfo.GetHeal() != 0 && eventInfo.GetActionTarget() != null && eventInfo.GetHitMask().HasAnyFlag(ProcFlagsHit.Critical);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            int absorb = MathFunctions.CalculatePct(eventInfo.GetHealInfo().GetHeal(), aurEff.GetAmount());
            if (absorb > 0)
                GetTarget().CastSpell(eventInfo.GetActionTarget(), SpellIds.DivineAegisAbsorb, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, absorb));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 1309969 - Power in Light: "Your Smite and Penance spells deal 15% increased damage to targets afflicted with your Holy Fire." Bound to
    // Smite (all ranks) and the Penance damage spells.
    [Script]
    class classic_spell_pri_power_in_light : SpellScript
    {
        void HandleCalcDamage(Unit victim, ref int damage, ref int flatMod, ref float pctMod)
        {
            Unit caster = GetCaster().ToUnit();
            AuraEffect talent = caster?.GetAuraEffect(SpellIds.PowerInLight, 0);
            if (talent == null || victim == null)
                return;

            // Holy Fire (all ranks): family flag 0x100000
            if (victim.GetAuraEffect(AuraType.PeriodicDamage, SpellFamilyNames.Priest, new FlagArray128(0x00100000, 0, 0, 0), caster.GetGUID()) != null)
                MathFunctions.AddPct(ref pctMod, talent.GetAmount());
        }

        public override void Register()
        {
            CalcDamage.Add(new(HandleCalcDamage));
        }
    }

    // 1310076 - Early Demise: "Increases Shadow Word: Death's critical strike chance on targets at or below 20% health by 30%." Effect 0 is the
    // crit bonus, effect 1 the health threshold. Bound to the Shadow Word: Death ranks.
    [Script]
    class classic_spell_pri_early_demise : SpellScript
    {
        void CalcCritChance(Unit victim, ref float critChance)
        {
            Unit caster = GetCaster().ToUnit();
            AuraEffect crit = caster?.GetAuraEffect(SpellIds.EarlyDemise, 0);
            AuraEffect threshold = caster?.GetAuraEffect(SpellIds.EarlyDemise, 1);
            if (victim == null || crit == null || threshold == null)
                return;

            if (victim.GetHealthPct() <= threshold.GetAmount())
                critChance += crit.GetAmount();
        }

        public override void Register()
        {
            OnCalcCritChance.Add(new(CalcCritChance));
        }
    }

    // 1317006 - Litany of Light: "When you cast a healing spell, gain Mana equal to 20% of the base cost of the spell if your previous heal was a
    // different spell." The talent aura (proc trigger spell without a spell) procs at cast (spell_proc); the energize is 1317007. Ranks of the
    // same spell count as the same spell (compared by name).
    [Script]
    class classic_spell_pri_litany_of_light : AuraScript
    {
        string _lastHeal = "";

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.LitanyOfLightEnergize);
        }

        static bool IsPenance(int spellId)
        {
            return spellId == 402174 || spellId == 1240720 || spellId == 1240721 || spellId == 1316995;
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            Spell spell = eventInfo.GetProcSpell();
            if (spell == null || spell.IsTriggered())
                return false;

            SpellInfo spellInfo = spell.GetSpellInfo();
            if (spellInfo.HasEffect(SpellEffectName.Heal) || spellInfo.HasAura(AuraType.PeriodicHeal))
                return true;

            // dummy cast spells: Prayer of Mending, and Penance cast on an ally
            if (ClassicPrayerOfMending.GetRank(spellInfo.Id) != null)
                return true;
            if (IsPenance(spellInfo.Id))
            {
                Unit target = spell.m_targets.GetUnitTarget();
                if (target != null)
                    return GetTarget().IsFriendlyTo(target);
            }
            return false;
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            SpellInfo spellInfo = eventInfo.GetProcSpell().GetSpellInfo();
            string name = spellInfo.SpellName[SharedConst.DefaultLocale];
            bool differentSpell = !string.IsNullOrEmpty(_lastHeal) && _lastHeal != name;
            _lastHeal = name;
            if (!differentSpell)
                return;

            Unit priest = GetTarget();
            int mana = MathFunctions.CalculatePct(ClassicSpells.GetBaseManaCost(spellInfo, priest), aurEff.GetAmount());
            if (mana > 0)
                priest.CastSpell(priest, SpellIds.LitanyOfLightEnergize, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, mana));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.ProcTriggerSpell));
        }
    }

    // 1277462, 1277634, 1277638, 1277639, 1277640 - Contingency Plan (ranks 1-5): "Place a Holy ward on an ally for 30 sec. The next time this
    // ally takes damage dropping their Health below 35%, they will gain a shield absorbing X damage and begin healing for Y Health over 15 sec."
    // Effect 0 is the health threshold; the rank's shield and heal over time are cast by the priest, then the ward is gone.
    [Script]
    class classic_spell_pri_contingency_plan : AuraScript
    {
        static (int Shield, int Hot) GetRankSpells(int spellId)
        {
            switch (spellId)
            {
                case 1277462: return (1277463, 1277456);
                case 1277634: return (1277464, 1277457);
                case 1277638: return (1277465, 1277458);
                case 1277639: return (1277467, 1277459);
                case 1277640: return (1277466, 1277460);
                default: return (0, 0);
            }
        }

        public override bool Validate(SpellInfo spellInfo)
        {
            var (shield, hot) = GetRankSpells(spellInfo.Id);
            return ValidateSpellInfo(shield, hot);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            Unit target = GetTarget();
            return damageInfo != null && damageInfo.GetDamage() != 0 && target.IsAlive() && target.HealthBelowPct(GetEffect(0).GetAmount());
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            Unit target = GetTarget();
            Unit caster = GetCaster();
            Unit source = caster ?? target;
            var (shield, hot) = GetRankSpells(GetId());

            CastSpellExtraArgs args = new(aurEff);
            args.SetOriginalCaster(GetCasterGUID());
            source.CastSpell(target, shield, args);
            source.CastSpell(target, hot, args);
            Remove();
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    /*######
    ## Paladin
    ######*/

    // 9799 - Eye for an Eye: "All critical strikes against you cause 15% of the damage taken to the attacker as well. The damage caused by Eye
    // for an Eye will not exceed 50% of the Paladin's total health." (25997; crit only + triggered via spell_proc)
    [Script]
    class classic_spell_pal_eye_for_an_eye : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.EyeForAnEyeDamage);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            Unit attacker = eventInfo.GetActor();
            return damageInfo != null && damageInfo.GetDamage() != 0 && eventInfo.GetHitMask().HasAnyFlag(ProcFlagsHit.Critical)
                && attacker != null && attacker != GetTarget() && attacker.IsAlive();
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            Unit paladin = GetTarget();
            int damage = MathFunctions.CalculatePct(eventInfo.GetDamageInfo().GetDamage(), aurEff.GetAmount());
            damage = (int)Math.Min(damage, paladin.CountPctFromMaxHealth(50));
            if (damage > 0)
                paladin.CastSpell(eventInfo.GetActor(), SpellIds.EyeForAnEyeDamage, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, damage));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 20127 - Redoubt: "Damaging melee attacks against you have a 10% chance to increase your chance to block by X%. Lasts 10 sec or 5 blocks."
    // (20128 with the talent amount; the 10% chance and the melee hits are the talent's own proc data)
    [Script]
    class classic_spell_pal_redoubt : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.RedoubtBuff);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            CastSpellExtraArgs args = new(aurEff);
            if (aurEff.GetAmount() > 0)
                args.AddSpellMod(SpellValueMod.BasePoint0, aurEff.GetAmount());
            GetTarget().CastSpell(GetTarget(), SpellIds.RedoubtBuff, args);
        }

        public override void Register()
        {
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 426065 - Infusion of Light: "Your Holy Shock and Flash of Light critical hits reduce the cast time of your next Holy Light cast within
    // 15 sec by 1.0 sec." Crits of Holy Shock / Flash of Light (spell_proc family mask) give 437063 (Holy Light cast time, 1 charge) with the
    // talent amount.
    [Script]
    class classic_spell_pal_infusion_of_light : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.InfusionOfLightBuff);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            CastSpellExtraArgs args = new(aurEff);
            if (aurEff.GetAmount() != 0)
                args.AddSpellMod(SpellValueMod.BasePoint0, aurEff.GetAmount());
            GetTarget().CastSpell(GetTarget(), SpellIds.InfusionOfLightBuff, args);
        }

        public override void Register()
        {
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 1280353 - Consecration (Consecrated Ground debuff): "Gives your Holy spells 10% increased damage against the first 4 enemies that enter your
    // Consecration." The debuff (Holy damage taken from the caster) has no points in the data: it takes the paladin's Consecrated Ground
    // (1310905) amount, nothing without the talent.
    [Script]
    class classic_spell_pal_consecrated_ground : AuraScript
    {
        void CalcAmount(AuraEffect aurEff, ref int amount, ref bool canBeRecalculated)
        {
            amount = 0;
            AuraEffect talent = GetCaster()?.GetAuraEffect(SpellIds.ConsecratedGround, 0);
            if (talent != null)
                amount = talent.GetAmount();
        }

        public override void Register()
        {
            DoEffectCalcAmount.Add(new(CalcAmount, 1, AuraType.ModSchoolMaskDamageFromCaster));
        }
    }

    // 1310911, 1311590, 1311595 - Light's Vigil (ranks 1-3): "Applies Light's Vigil to the target for 30 sec. Your next Holy Shock cast on them
    // triggers no cooldown and causes enemy targets to suffer X Holy damage and refund 75% of Light's Vigil's Mana cost, or allied targets to
    // heal their party for Y. You may only have one Light's Vigil active per party."
    static class ClassicLightsVigil
    {
        public record Rank(int Cast, int AllyAura, int EnemyAura, int PartyHeal, int Damage);

        public static readonly Rank[] Ranks =
        {
            new(1310911, 1310909, 1310910, 1310912, 1310914),
            new(1311590, 1311593, 1311594, 1311591, 1311592),
            new(1311595, 1311597, 1311599, 1311596, 1311598),
        };

        public static Rank GetRank(int castSpellId) { return Ranks.FirstOrDefault(rank => rank.Cast == castSpellId); }

        public static bool IsAura(int spellId) { return Ranks.Any(rank => rank.AllyAura == spellId || rank.EnemyAura == spellId); }
    }

    // The cast puts the ally or enemy version on the target. One Light's Vigil per paladin: the previous one is removed, and the new one is
    // registered as the paladin's single target aura (removed with the paladin like the Limit N auras).
    [Script]
    class classic_spell_pal_lights_vigil : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            var rank = ClassicLightsVigil.GetRank(spellInfo.Id);
            return rank != null && ValidateSpellInfo(rank.AllyAura, rank.EnemyAura, rank.PartyHeal, rank.Damage, SpellIds.LightsVigilEnergize);
        }

        void HandleDummy(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            Unit target = GetHitUnit();
            if (target == null)
                return;

            foreach (Aura previous in caster.GetSingleCastAuras().Where(aura => ClassicLightsVigil.IsAura(aura.GetId())).ToList())
                previous.Remove();

            var rank = ClassicLightsVigil.GetRank(GetSpellInfo().Id);
            int auraId = caster.IsFriendlyTo(target) ? rank.AllyAura : rank.EnemyAura;
            caster.CastSpell(target, auraId, new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetTriggeringSpell(GetSpell()));

            Aura vigil = target.GetAura(auraId, caster.GetGUID());
            if (vigil != null && !vigil.IsSingleTarget())
            {
                vigil.SetIsSingleTarget(true);
                caster.GetSingleCastAuras().Insert(0, vigil);
            }
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 1310909, 1310910, 1311593, 1311594, 1311597, 1311599 - Light's Vigil (marks): the data gives them one charge on any helpful / harmful spell
    // taken; only the paladin's Holy Shock consumes them (classic_spell_pal_holy_shock_lights_vigil), so they never proc by themselves
    [Script]
    class classic_spell_pal_lights_vigil_mark : AuraScript
    {
        bool CheckProc(ProcEventInfo eventInfo)
        {
            return false;
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
        }
    }

    // 20473, 20929, 20930, 1311606 - Holy Shock (all ranks, extra script): on a target with the paladin's Light's Vigil the mark is consumed,
    // enemies take the rank's Holy damage and the paladin gets back 75% (Light's Vigil effect 1) of Light's Vigil's mana cost, allies heal
    // their party; the Holy Shock cooldown is reset after the cast
    [Script]
    class classic_spell_pal_holy_shock_lights_vigil : SpellScript
    {
        bool _vigilConsumed;

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.LightsVigilEnergize);
        }

        void HandleHit(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            Unit target = GetHitUnit();
            if (target == null)
                return;

            foreach (var rank in ClassicLightsVigil.Ranks)
            {
                Aura vigil = target.GetAura(rank.AllyAura, caster.GetGUID()) ?? target.GetAura(rank.EnemyAura, caster.GetGUID());
                if (vigil == null)
                    continue;

                vigil.Remove();
                _vigilConsumed = true;

                CastSpellExtraArgs args = new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetTriggeringSpell(GetSpell());
                if (caster.IsFriendlyTo(target))
                    caster.CastSpell(target, rank.PartyHeal, args);
                else
                {
                    caster.CastSpell(target, rank.Damage, args);

                    SpellInfo vigilInfo = SpellMgr.GetSpellInfo(rank.Cast, Difficulty.None);
                    int refundPct = vigilInfo.GetEffects().Count > 1 ? vigilInfo.GetEffect(1).CalcValue(caster) : 75;
                    int mana = MathFunctions.CalculatePct(ClassicSpells.GetBaseManaCost(vigilInfo, caster), refundPct);
                    if (mana > 0)
                        caster.CastSpell(caster, SpellIds.LightsVigilEnergize, new CastSpellExtraArgs(TriggerCastFlags.FullMask)
                            .SetTriggeringSpell(GetSpell()).AddSpellMod(SpellValueMod.BasePoint0, mana));
                }
                break;
            }
        }

        void HandleAfterCast()
        {
            if (_vigilConsumed)
                GetCaster().ToUnit()?.GetSpellHistory().ResetCooldown(GetSpellInfo().Id, true);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleHit, 0, SpellEffectName.Dummy));
            AfterCast.Add(new(HandleAfterCast));
        }
    }

    // 1310912, 1311591, 1311596 - Light's Vigil (heal): "heal their party": only the target and its party members in the area
    [Script]
    class classic_spell_pal_lights_vigil_party_heal : SpellScript
    {
        void FilterTargets(List<WorldObject> targets)
        {
            Unit target = GetExplTargetUnit();
            targets.RemoveAll(obj =>
            {
                Unit unit = obj.ToUnit();
                return unit == null || target == null || (unit != target && !unit.IsInPartyWith(target));
            });
        }

        public override void Register()
        {
            OnObjectAreaTargetSelect.Add(new(FilterTargets, 0, Targets.UnitDestAreaAlly));
        }
    }

    // 1311606 - Holy Shock (Forever talent, level 30): "Blasts the target with Holy energy, causing Holy damage to an enemy, or healing to an
    // ally." The dummy casts 1311604 (damage) or 1311605 (heal); same handling as classic_spell_pal_holy_shock for the trainer ranks.
    [Script]
    class classic_spell_pal_holy_shock_forever : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.HolyShockForeverDamage, SpellIds.HolyShockForeverHeal);
        }

        SpellCastResult CheckCast()
        {
            ClassicSpells.IsFriendlyHolyTargetValid(GetCaster().ToUnit(), GetExplTargetUnit(), out SpellCastResult result);
            return result;
        }

        void HandleDummy(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            Unit target = GetHitUnit();
            if (target == null)
                return;

            caster.CastSpell(target, caster.IsFriendlyTo(target) ? SpellIds.HolyShockForeverHeal : SpellIds.HolyShockForeverDamage, new CastSpellExtraArgs(GetSpell()));
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 20271 - Judgement (extra script) for 1311074 Sanctified Judgement: "Gives your Judgement ability a 100% chance to return 60% of the Mana
    // cost of the judged seal." Effect 0 is the chance, effect 1 the percentage; the energize is 1311077.
    [Script]
    class classic_spell_pal_sanctified_judgement : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.SanctifiedJudgementEnergize);
        }

        void HandleScript(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            AuraEffect chance = caster.GetAuraEffect(SpellIds.SanctifiedJudgement, 0);
            AuraEffect pct = caster.GetAuraEffect(SpellIds.SanctifiedJudgement, 1);
            if (chance == null || pct == null || !RandomHelper.randChance(chance.GetAmount()))
                return;

            foreach (var (auraId, aurApp) in caster.GetAppliedAuras())
            {
                if (ClassicSpells.GetSealJudgement(auraId) == 0 || aurApp.GetBase().GetCasterGUID() != caster.GetGUID())
                    continue;

                int mana = MathFunctions.CalculatePct(ClassicSpells.GetBaseManaCost(aurApp.GetBase().GetSpellInfo(), caster), pct.GetAmount());
                if (mana > 0)
                    caster.CastSpell(caster, SpellIds.SanctifiedJudgementEnergize, new CastSpellExtraArgs(TriggerCastFlags.FullMask)
                        .SetTriggeringSpell(GetSpell()).AddSpellMod(SpellValueMod.BasePoint0, mana));
                break;
            }
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleScript, 0, SpellEffectName.ScriptEffect));
        }
    }

    // 1310927 - Light's Fury (the Seal of Fury shield) for 1314103 Improved Seal of Fury: "When Seal of Fury's shield is fully absorbed, restore
    // X Mana, increased by 15% per level the attacker is above you, up to 45%." Effect 0 is the mana (per level), effect 1 the percentage per
    // level, effect 2 the level cap; the energize is 1314104.
    [Script]
    class classic_spell_pal_improved_seal_of_fury : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ImprovedSealOfFuryEnergize);
        }

        void AfterAbsorb(AuraEffect aurEff, DamageInfo dmgInfo, ref int absorbAmount)
        {
            // the remaining amount is reduced after this hook: fully absorbed when this absorb uses it up
            if (absorbAmount < aurEff.GetAmount())
                return;

            Unit paladin = GetTarget();
            AuraEffect mana = paladin.GetAuraEffect(SpellIds.ImprovedSealOfFury, 0);
            if (mana == null)
                return;

            int amount = mana.GetAmount();
            Unit attacker = dmgInfo.GetAttacker();
            if (attacker != null)
            {
                AuraEffect pctPerLevel = paladin.GetAuraEffect(SpellIds.ImprovedSealOfFury, 1);
                AuraEffect maxLevels = paladin.GetAuraEffect(SpellIds.ImprovedSealOfFury, 2);
                int levels = Math.Clamp(attacker.GetLevel() - paladin.GetLevel(), 0, maxLevels != null ? maxLevels.GetAmount() : 0);
                if (pctPerLevel != null && levels > 0)
                    amount += MathFunctions.CalculatePct(amount, pctPerLevel.GetAmount() * levels);
            }

            if (amount > 0)
                paladin.CastSpell(paladin, SpellIds.ImprovedSealOfFuryEnergize, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, amount));
        }

        public override void Register()
        {
            AfterEffectAbsorb.Add(new(AfterAbsorb, 0));
        }
    }

    // Holy Light (635, 639, 647, 1026, 1042, 3472, 10328, 10329, 25292) and Flash of Light (19750, 19939-19943):
    // - 19977, 19978, 19979 Blessing of Light (and 25890 Greater Blessing of Light): "increasing the effects of Holy Light spells used on the
    //   target by up to X and the effect Flash of Light spells used on the target by up to Y". Effect 0 is the Holy Light bonus, effect 1 the
    //   Flash of Light bonus; the best blessing on the target counts (no stacking between paladins), scaled by the heal coefficient (vanilla).
    // - 412019 Sacred Shield: Flash of Light gets the shield's crit bonus (412018 effect 1, +50%) on targets with the paladin's Sacred Shield.
    [Script]
    class classic_spell_pal_holy_light_flash_of_light : SpellScript
    {
        static bool IsHolyLight(SpellInfo spellInfo)
        {
            return (spellInfo.SpellFamilyFlags[0] & 0x80000000) != 0;
        }

        static bool IsBlessingOfLight(int spellId)
        {
            return spellId == 19977 || spellId == 19978 || spellId == 19979 || spellId == 25890;
        }

        void HandleCalcHealing(Unit victim, ref int healing, ref int flatMod, ref float pctMod)
        {
            if (victim == null)
                return;

            int blessingEffect = IsHolyLight(GetSpellInfo()) ? 0 : 1;
            int bonus = 0;
            foreach (AuraEffect aurEff in victim.GetAuraEffectsByType(AuraType.Dummy))
                if (aurEff.GetEffIndex() == blessingEffect && IsBlessingOfLight(aurEff.GetId()))
                    bonus = Math.Max(bonus, aurEff.GetAmount());

            // the heal is effect 0 of both spells
            if (bonus > 0)
                flatMod += (int)(bonus * GetSpellInfo().GetEffect(0).BonusCoefficient);
        }

        void CalcCritChance(Unit victim, ref float critChance)
        {
            if (victim == null || IsHolyLight(GetSpellInfo()))
                return;

            AuraEffect sacredShield = victim.GetAuraEffect(SpellIds.SacredShieldAbsorb, 1, GetCaster().GetGUID());
            if (sacredShield != null)
                critChance += sacredShield.GetAmount();
        }

        public override void Register()
        {
            CalcHealing.Add(new(HandleCalcHealing));
            OnCalcCritChance.Add(new(CalcCritChance));
        }
    }

    // 412019 - Sacred Shield: "Each time the target takes damage they gain a Sacred Shield, absorbing [36 * (38.258376 + 0.904195 * L + 0.161311 *
    // L * L) / 100] damage and increasing the Paladin's chance to critically hit with Flash of Light by 50% for up to 6 sec. In addition, causes
    // your Flash of Light to heal targets with Sacred Shield for an additional 100% over 12 sec. They cannot gain this effect more than once
    // every 6 sec. Lasts 1 min." Damage taken: 412018 (6 sec, effect 0 of 412019 is the 6 sec limit); the paladin's Flash of Light: 412020
    // (12 sec heal over time, effect 2 of 412019 is the percentage). One target per paladin is the spell's own Limit N attribute.
    [Script]
    class classic_spell_pal_sacred_shield : AuraScript
    {
        DateTime _nextShield = DateTime.MinValue;

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.SacredShieldAbsorb, SpellIds.SacredShieldFlashOfLight)
                && ValidateSpellEffect((spellInfo.Id, 2));
        }

        static bool IsFlashOfLight(SpellInfo spellInfo)
        {
            return spellInfo != null && spellInfo.SpellFamilyName == SpellFamilyNames.Paladin && (spellInfo.SpellFamilyFlags[0] & 0x40000000) != 0;
        }

        static bool IsDamageTaken(ProcEventInfo eventInfo)
        {
            return eventInfo.GetTypeMask().HasAnyFlag(ProcFlags.TakeAnyDamage);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            if (IsDamageTaken(eventInfo))
            {
                DamageInfo damageInfo = eventInfo.GetDamageInfo();
                return damageInfo != null && (damageInfo.GetDamage() != 0 || damageInfo.GetAbsorb() != 0) && DateTime.UtcNow >= _nextShield;
            }

            HealInfo healInfo = eventInfo.GetHealInfo();
            return healInfo != null && healInfo.GetHeal() != 0 && eventInfo.GetActor() != null && eventInfo.GetActor().GetGUID() == GetCasterGUID()
                && IsFlashOfLight(healInfo.GetSpellInfo());
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            Unit target = GetTarget();
            Unit caster = GetCaster();
            if (caster == null)
                return;

            if (IsDamageTaken(eventInfo))
            {
                _nextShield = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Max(aurEff.GetAmount(), 1));

                SpellInfo shieldInfo = SpellMgr.GetSpellInfo(SpellIds.SacredShieldAbsorb, Difficulty.None);
                float level = caster.GetLevel();
                float scale = 38.258376f + 0.904195f * level + 0.161311f * level * level;
                int absorb = (int)(shieldInfo.GetEffect(0).CalcValue(caster) * scale / 100.0f);
                if (absorb > 0)
                    caster.CastSpell(target, SpellIds.SacredShieldAbsorb, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, absorb));
                return;
            }

            SpellInfo hotInfo = SpellMgr.GetSpellInfo(SpellIds.SacredShieldFlashOfLight, Difficulty.None);
            int period = (int)hotInfo.GetEffect(0).ApplyAuraPeriod;
            int ticks = period > 0 ? Math.Max(hotInfo.GetMaxDuration() / period, 1) : 1;
            AuraEffect pct = GetEffect(2);
            int perTick = MathFunctions.CalculatePct(eventInfo.GetHealInfo().GetHeal(), pct != null ? pct.GetAmount() : 100) / ticks;
            if (perTick > 0)
                caster.CastSpell(target, SpellIds.SacredShieldFlashOfLight, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, perTick));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }
}
