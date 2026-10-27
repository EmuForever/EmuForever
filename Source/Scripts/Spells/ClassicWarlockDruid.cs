// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Dynamic;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;
using System.Collections.Generic;
using static Global;

// Classic 1.60 (WoW Forever) warlock and druid talents and abilities whose client spell is only a dummy aura or dummy effect, ported from the
// reference TrinityCore (src/server/scripts/Custom/Classic/world/talents/classic_talents_warlock_druid.cpp). Talent ranks come from
// TraitDefinitionEffectPoints curves: the core puts the rank value into the aura amount, so the scripts read aurEff.GetAmount() and never
// hardcode rank values. Tooltip formulas quoted below are the raw client Spell.db2 ones.
namespace Scripts.Spells.Classic.WarlockDruid
{
    struct SpellIds
    {
        // Warlock
        public const int LifeTapEnergize = 31818;           // energize mana (bp = mana gained)
        public const int ImprovedLifeTap = 18182;
        public const int Metamorphosis = 403816;            // effect 3: "increasing mana gained from Life Tap by $403816s4%"
        public const int DemonicEnergies = 1225214;
        public const int DemonicEnergiesHeal = 1243028;     // heal, targets the caster's pet
        public const int ShadowVulnerability = 17794;       // SPELL_AURA_MOD_SCHOOL_MASK_DAMAGE_FROM_CASTER, shadow, 12 sec
        public const int SoulSiphon = 17804;
        public const int MasterDemonologist = 23785;
        public const int MdImp = 23759;                     // area aura pet: fire damage done
        public const int MdVoidwalker = 23760;              // area aura pet: physical damage taken
        public const int MdSuccubus = 23761;                // area aura pet: shadow damage done
        public const int MdFelhunter = 23762;               // area aura pet: magic damage taken
        public const int DemonicKnowledge = 412732;
        public const int DemonicKnowledgeOwner = 1243120;   // area aura owner: spell damage + healing done (cast by the demon)
        public const int DemonicKnowledgePet = 412735;      // spell damage + healing done on the demon itself
        public const int DemonicPact = 425464;
        public const int BurningShadow = 18789;             // Demonic Sacrifice: Imp
        public const int FelStamina = 18790;                // Demonic Sacrifice: Voidwalker
        public const int TouchOfFire = 18791;               // Demonic Sacrifice: Succubus / Incubus
        public const int FelEnergy = 18792;                 // Demonic Sacrifice: Felhunter
        public const int ShadowAndFlame = 426316;
        public const int ShadowAndFlameShadow = 1293816;    // "Shadow": shadow damage done, 20 sec
        public const int ShadowAndFlameFire = 426311;       // "Flame": fire damage done, 20 sec
        public const int ImmolateMarker = 1282590;          // hidden Immolate aura required by Conflagrate (classic_spell_warl_immolate)
        public const int SoulHarvest = 437032;
        public const int SoulHarvestBuff = 1242853;         // mana regen % + regen while casting, 10 sec
        public const int BaneOfHavoc = 1225228;
        public const int BaneOfHavocTracker = 1243338;      // warlock side proc aura
        public const int BaneOfHavocDamage = 1243339;
        public const int ImprovedHealthstoneR1 = 18692;
        public const int ImprovedHealthstoneR2 = 18693;
        public const int GrimoireOfSynergy = 426301;
        public const int GrimoireOfSynergyBuff = 426303;    // all damage done, 15 sec

        // Druid
        public const int ThickHide = 16929;
        public const int ThickHideArmor = 9970701;          // serverside: SPELL_AURA_MOD_BASE_RESISTANCE armor (bp = armor)
        public const int BloodFrenzyRage = 16959;
        public const int Furor = 17056;
        public const int FurorRage = 17057;
        public const int EclipseBuff = 408255;
        public const int DreamstateBuff = 408261;
        public const int KingOfTheJungle = 417046;
        public const int KingOfTheJungleEnergize = 417045;
        public const int RendAndTear = 1223246;
    }

    struct Misc
    {
        public const int SkillAffliction = 355;
        public const int ItemSoulShard = 6265;

        public const int NpcImp = 416;
        public const int NpcFelhunter = 417;
        public const int NpcVoidwalker = 1860;
        public const int NpcSuccubus = 1863;
        public const int NpcIncubus = 185317;
        public const int NpcFelguard = 213450;
    }

    enum DemonKind
    {
        None,
        Imp,
        Voidwalker,
        Succubus,
        Felhunter,
        Felguard
    }

    static class ClassicWarlockDruid
    {
        public static DemonKind GetDemonKind(int entry)
        {
            switch (entry)
            {
                case Misc.NpcImp: return DemonKind.Imp;
                case Misc.NpcVoidwalker: return DemonKind.Voidwalker;
                case Misc.NpcSuccubus:
                case Misc.NpcIncubus: return DemonKind.Succubus;
                case Misc.NpcFelhunter: return DemonKind.Felhunter;
                case Misc.NpcFelguard: return DemonKind.Felguard;
                default: return DemonKind.None;
            }
        }

        // Demonic Sacrifice buff of each demon (VMaNGOS WarlockDemonicSacrificeScript)
        public static int GetSacrificeBuff(DemonKind kind)
        {
            switch (kind)
            {
                case DemonKind.Imp: return SpellIds.BurningShadow;
                case DemonKind.Voidwalker: return SpellIds.FelStamina;
                case DemonKind.Succubus: return SpellIds.TouchOfFire;
                case DemonKind.Felhunter: return SpellIds.FelEnergy;
                default: return 0;
            }
        }

        public static readonly int[] SacrificeBuffs = { SpellIds.BurningShadow, SpellIds.FelStamina, SpellIds.TouchOfFire, SpellIds.FelEnergy };

        public static Pet GetActiveDemon(Player player) { return GetActiveDemon(player, out _); }

        public static Pet GetActiveDemon(Player player, out DemonKind kind)
        {
            kind = DemonKind.None;
            Pet pet = player.GetPet();
            if (pet == null || !pet.IsInWorld || !pet.IsAlive())
                return null;

            DemonKind demon = GetDemonKind(pet.GetEntry());
            if (demon == DemonKind.None)
                return null;

            kind = demon;
            return pet;
        }

        // Applies an aura cast by the demon on itself (area auras reach the warlock) and forces its effect amounts
        // (the helper spells carry the base rank values or per level points, the talent amounts come from the trait curves)
        static void SetDemonAura(Unit caster, Unit target, int spellId, params (int EffIndex, int Amount)[] amounts)
        {
            Aura aura = target.GetAura(spellId, caster.GetGUID());
            if (aura == null)
            {
                caster.CastSpell(target, spellId, new CastSpellExtraArgs(TriggerCastFlags.FullMask));
                aura = target.GetAura(spellId, caster.GetGUID());
                if (aura == null)
                    return;
            }

            foreach (var (effIndex, amount) in amounts)
            {
                AuraEffect aurEff = aura.GetEffect(effIndex);
                if (aurEff != null && aurEff.GetAmount() != amount)
                    aurEff.ChangeAmount(amount);
            }
        }

        // 23785 - Master Demonologist: "Grants both the Warlock and the summoned demon an effect as long as that demon is active.
        // Imp - Increases Fire damage done by $m1%. Voidwalker - Reduces Physical damage taken by $m2%. Succubus/Incubus - Increases Shadow
        // damage done by $m3%. Felhunter - Reduces Magic damage taken by $m4%." The vanilla area auras 23759..23762 (cast by the demon, area
        // aura pet: the demon and its owner) carry the effect.
        static void SyncMasterDemonologist(Player player, Pet pet, DemonKind kind)
        {
            var demonAuras = new (DemonKind Kind, int SpellId, int TalentEffect, int Sign)[]
            {
                (DemonKind.Imp, SpellIds.MdImp, 0, 1),
                (DemonKind.Voidwalker, SpellIds.MdVoidwalker, 1, -1),
                (DemonKind.Succubus, SpellIds.MdSuccubus, 2, 1),
                (DemonKind.Felhunter, SpellIds.MdFelhunter, 3, -1),
            };

            foreach (var demonAura in demonAuras)
            {
                int amount = 0;
                if (demonAura.Kind == kind)
                {
                    AuraEffect talent = player.GetAuraEffect(SpellIds.MasterDemonologist, demonAura.TalentEffect);
                    if (talent != null)
                        amount = talent.GetAmount() * demonAura.Sign;
                }

                if (amount == 0)
                {
                    pet.RemoveAurasDueToSpell(demonAura.SpellId, pet.GetGUID());
                    continue;
                }

                SetDemonAura(pet, pet, demonAura.SpellId, (0, amount));
            }
        }

        // 412732 - Demonic Knowledge: "Increases your spell damage and your Demon pet's spell damage by up to $s1% of your level while you have
        // a summoned Demon pet active." 1243120 (cast by the demon, area aura owner) buffs the warlock, 412735 the demon.
        static void SyncDemonicKnowledge(Player player, Pet pet)
        {
            int bonus = 0;
            AuraEffect talent = player.GetAuraEffect(SpellIds.DemonicKnowledge, 0);
            if (talent != null)
                bonus = player.GetLevel() * talent.GetAmount() / 100;

            if (bonus <= 0)
            {
                pet.RemoveAurasDueToSpell(SpellIds.DemonicKnowledgeOwner, pet.GetGUID());
                pet.RemoveAurasDueToSpell(SpellIds.DemonicKnowledgePet, pet.GetGUID());
                return;
            }

            SetDemonAura(pet, pet, SpellIds.DemonicKnowledgeOwner, (0, bonus), (1, bonus));

            bool applied = pet.HasAura(SpellIds.DemonicKnowledgePet, pet.GetGUID());
            SetDemonAura(pet, pet, SpellIds.DemonicKnowledgePet, (0, bonus), (1, bonus));
            if (!applied)
            {
                // the client spell lasts 15 sec, the talent lasts as long as the demon
                Aura aura = pet.GetAura(SpellIds.DemonicKnowledgePet, pet.GetGUID());
                if (aura != null)
                {
                    aura.SetMaxDuration((Milliseconds)(-1));
                    aura.SetDuration((Milliseconds)(-1));
                }
            }
        }

        static void SyncDemonTalents(Player player)
        {
            if (!player.IsInWorld)
                return;

            Pet pet = GetActiveDemon(player, out DemonKind kind);
            if (pet == null)
                return;

            SyncMasterDemonologist(player, pet, kind);
            SyncDemonicKnowledge(player, pet);
        }

        // aura hooks (heartbeat, apply/remove) run while the unit iterates or changes its auras: apply the demon auras on the next update
        public static void ScheduleDemonTalentSync(Unit unit, int delayMs = 0)
        {
            Player player = unit?.ToPlayer();
            if (player == null || !player.IsInWorld)
                return;

            player.m_Events.AddEventAtOffset(() => SyncDemonTalents(player), TimeSpan.FromMilliseconds(delayMs));
        }

        public static bool IsAfflictionSpell(int spellId)
        {
            foreach (var skillLine in SpellMgr.GetSkillLineAbilityMapBounds(spellId))
                if ((int)skillLine.SkillLine == Misc.SkillAffliction)
                    return true;
            return false;
        }

        static bool IsThickHideForm(ShapeShiftForm form)
        {
            return form == ShapeShiftForm.CatForm || form == ShapeShiftForm.BearForm || form == ShapeShiftForm.DireBearForm || form == ShapeShiftForm.MoonkinForm;
        }

        // 16929 - Thick Hide: "While in Bear Form, Cat Form, Dire Bear Form, or Moonkin Form, you gain $s2 additional base Armor per level and
        // another ${$m1/100}.2 base Armor for each point of defense skill beyond five times your level. This amount can be further increased
        // by multipliers from those forms." The serverside helper adds it as base armor (SPELL_AURA_MOD_BASE_RESISTANCE: the form armor
        // multipliers apply on top).
        public static void UpdateThickHide(Unit druid)
        {
            AuraEffect perDefense = druid.GetAuraEffect(SpellIds.ThickHide, 0);
            AuraEffect perLevel = druid.GetAuraEffect(SpellIds.ThickHide, 1);
            if (perDefense == null || perLevel == null || !IsThickHideForm(druid.GetShapeshiftForm()))
            {
                druid.RemoveAurasDueToSpell(SpellIds.ThickHideArmor);
                return;
            }

            int level = druid.GetLevel();
            // the client data value is per level (3, the Wowhead tooltip shows the level 60 total 180); a curve value above 10 is already a total
            double armor = perLevel.GetAmount() <= 10 ? perLevel.GetAmount() * level : perLevel.GetAmount();
            Player player = druid.ToPlayer();
            if (player != null)
            {
                int extraDefense = player.GetSkillValue(SkillType.Defense) - level * 5;
                if (extraDefense > 0)
                    armor += extraDefense * perDefense.GetAmount() / 100.0;
            }

            int total = (int)Math.Floor(armor);
            if (total <= 0)
            {
                druid.RemoveAurasDueToSpell(SpellIds.ThickHideArmor);
                return;
            }

            AuraEffect current = druid.GetAuraEffect(SpellIds.ThickHideArmor, 0);
            if (current != null)
            {
                if (current.GetAmount() != total)
                    current.ChangeAmount(total);
                return;
            }

            druid.CastSpell(druid, SpellIds.ThickHideArmor, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.BasePoint0, total));
        }
    }

    /*######
    ## Warlock
    ######*/

    // 1454, 1455, 1456, 11687, 11688, 11689 - Life Tap (client: dummy effect, nothing happened)
    // Vanilla (VMaNGOS WarlockLifeTapScript): converts health into the same amount of mana. The health cost is the effect value plus 80% of
    // the warlock's shadow spell damage (VMaNGOS spell_template coefficient 0.8, reduced for ranks learned below level 20); the spell fails
    // when the warlock has no more health than that. 18182 Improved Life Tap: "Increases the amount of Mana awarded by your Life Tap spell
    // by $s1%". 403816 Metamorphosis (effect 3) adds its own %. 1225214 Demonic Energies (effect 1): "When you gain Mana from Life Tap, your
    // summoned demon gains $m2% of the Mana you gain."
    [Script]
    class classic_talent_warl_life_tap : SpellScript
    {
        const float LifeTapSpellPowerCoefficient = 0.8f;

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.LifeTapEnergize);
        }

        int CalcHealthCost()
        {
            Unit caster = GetCaster().ToUnit();
            SpellInfo spellInfo = GetSpellInfo();

            float coefficient = LifeTapSpellPowerCoefficient;
            // vanilla level penalty (Unit::SpellDamageBonusDone ClassicSpellLevelPenalty)
            if (spellInfo.SpellLevel > 0 && spellInfo.SpellLevel < 20)
                coefficient *= 1.0f - (20.0f - spellInfo.SpellLevel) * 0.0375f;

            double cost = GetEffectInfo(0).CalcValue(caster);
            cost += caster.SpellBaseDamageBonusDone(spellInfo.GetSchoolMask()) * coefficient;
            return Math.Max((int)cost, 0);
        }

        SpellCastResult CheckCast()
        {
            if (GetCaster().ToUnit().GetHealth() <= CalcHealthCost())
                return SpellCastResult.Fizzle;
            return SpellCastResult.SpellCastOk;
        }

        void HandleDummy(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            int cost = CalcHealthCost();
            if (cost <= 0 || caster.GetHealth() <= cost)
                return;

            // not shown in the combat log (vanilla)
            caster.ModifyHealth(-cost);

            int mana = cost;
            AuraEffect improved = caster.GetAuraEffect(SpellIds.ImprovedLifeTap, 0);
            if (improved != null)
                MathFunctions.AddPct(ref mana, improved.GetAmount());
            AuraEffect metamorphosis = caster.GetAuraEffect(SpellIds.Metamorphosis, 3);
            if (metamorphosis != null)
                MathFunctions.AddPct(ref mana, metamorphosis.GetAmount());

            if (mana <= 0)
                return;

            caster.CastSpell(caster, SpellIds.LifeTapEnergize, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.BasePoint0, mana));

            AuraEffect energies = caster.GetAuraEffect(SpellIds.DemonicEnergies, 1);
            Player player = caster.ToPlayer();
            Pet pet = player != null ? ClassicWarlockDruid.GetActiveDemon(player) : null;
            if (energies != null && pet != null && pet.GetMaxPower(PowerType.Mana) > 0)
            {
                int petMana = MathFunctions.CalculatePct(mana, energies.GetAmount());
                if (petMana > 0)
                    caster.EnergizeBySpell(pet, GetSpellInfo(), petMana, PowerType.Mana);
            }
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 1225214 - Demonic Energies (effect 0): "You heal your pet for $s1% of all spell damage you deal." (proc: harmful spell + periodic)
    // The Life Tap part (effect 1) is in classic_talent_warl_life_tap.
    [Script]
    class classic_talent_warl_demonic_energies : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.DemonicEnergiesHeal);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            if (damageInfo == null || damageInfo.GetDamage() == 0)
                return false;

            Player player = GetTarget().ToPlayer();
            Pet pet = player != null ? ClassicWarlockDruid.GetActiveDemon(player) : null;
            return pet != null && pet.GetHealth() < pet.GetMaxHealth();
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            int heal = MathFunctions.CalculatePct(eventInfo.GetDamageInfo().GetDamage(), aurEff.GetAmount());
            if (heal <= 0)
                return;

            // 1243028 targets the caster's pet
            GetTarget().CastSpell(GetTarget(), SpellIds.DemonicEnergiesHeal, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, heal));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 17793 - Improved Shadow Bolt: "Your Shadow Bolt critical strikes increase Shadow damage taken by the target from your attacks by $m1%
    // for $17794d." 17794 Shadow Vulnerability is SPELL_AURA_MOD_SCHOOL_MASK_DAMAGE_FROM_CASTER (shadow).
    [Script]
    class classic_talent_warl_improved_shadow_bolt : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ShadowVulnerability);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            // Shadow Bolt: warlock family flag 0x1
            return spellInfo != null && spellInfo.SpellFamilyName == SpellFamilyNames.Warlock && (spellInfo.SpellFamilyFlags[0] & 0x1) != 0
                && eventInfo.GetHitMask().HasAnyFlag(ProcFlagsHit.Critical) && eventInfo.GetActionTarget() != null;
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            CastSpellExtraArgs args = new(aurEff);
            if (aurEff.GetAmount() > 0)
                args.AddSpellMod(SpellValueMod.BasePoint0, aurEff.GetAmount());
            GetTarget().CastSpell(eventInfo.GetActionTarget(), SpellIds.ShadowVulnerability, args);
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 689, 699, 709, 7651, 11699, 11700 - Drain Life; 1120, 8288, 8289, 11675 - Drain Soul; 1316697 - Wrack
    // 17804 - Soul Siphon: "Increases the damage done or health drained by your Drain Life, Drain Soul, and Wrack spells by $s1% per each
    // of your other Affliction effects active on the target, up to a maximum increase of ${$m1*3}%." Affliction effects: the warlock's auras
    // on the target from the Affliction skill line (355). Drain Life heals for the damage it deals, so the health drained follows.
    [Script]
    class classic_talent_warl_soul_siphon : AuraScript
    {
        void CalcDamage(AuraEffect aurEff, Unit victim, ref int damage, ref int flatMod, ref float pctMod)
        {
            if (aurEff.GetAuraType() != AuraType.PeriodicDamage && aurEff.GetAuraType() != AuraType.PeriodicLeech)
                return;

            Unit caster = GetCaster();
            if (caster == null || victim == null)
                return;

            AuraEffect talent = caster.GetAuraEffect(SpellIds.SoulSiphon, 0);
            if (talent == null || talent.GetAmount() <= 0)
                return;

            int effects = 0;
            foreach (var (spellId, aurApp) in victim.GetAppliedAuras())
            {
                if (spellId == GetId() || aurApp.GetBase().GetCasterGUID() != caster.GetGUID())
                    continue;

                if (ClassicWarlockDruid.IsAfflictionSpell(spellId))
                    ++effects;
            }

            if (effects == 0)
                return;

            float bonus = Math.Min(effects * talent.GetAmount(), talent.GetAmount() * 3.0f);
            MathFunctions.AddPct(ref pctMod, bonus);
        }

        public override void Register()
        {
            DoEffectCalcDamageAndHealing.Add(new(CalcDamage, SpellConst.EffectAll, AuraType.Any));
        }
    }

    // 1120, 8288, 8289, 11675 - Drain Soul
    // 437032 - Soul Harvest: "Killing a non-trivial target afflicted by your Drain Soul increases your Mana regeneration by $m1% for
    // $1242853d and allows $m2% of normal Mana regeneration to continue while casting."
    [Script]
    class classic_talent_warl_soul_harvest : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.SoulHarvestBuff) && ValidateSpellEffect((spellInfo.Id, 1));
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            if (GetTargetApplication().GetRemoveMode() != AuraRemoveMode.Death)
                return;

            Player warlock = GetCaster()?.ToPlayer();
            if (warlock == null || !warlock.IsHonorOrXPTarget(GetTarget()))
                return;

            AuraEffect regen = warlock.GetAuraEffect(SpellIds.SoulHarvest, 0);
            AuraEffect whileCasting = warlock.GetAuraEffect(SpellIds.SoulHarvest, 1);
            if (regen == null || whileCasting == null)
                return;

            warlock.CastSpell(warlock, SpellIds.SoulHarvestBuff, new CastSpellExtraArgs(TriggerCastFlags.FullMask)
                .AddSpellMod(SpellValueMod.BasePoint0, regen.GetAmount())
                .AddSpellMod(SpellValueMod.BasePoint1, whileCasting.GetAmount()));
        }

        public override void Register()
        {
            AfterEffectRemove.Add(new(HandleRemove, 1, AuraType.PeriodicDamage, AuraEffectHandleModes.Real));
        }
    }

    // 23785 - Master Demonologist and 412732 - Demonic Knowledge (see ClassicWarlockDruid). The demon auras are refreshed when the talent is
    // learned or removed, every heartbeat and right after a demon is summoned.
    [Script]
    class classic_talent_warl_demon_talent_sync : AuraScript
    {
        void HandleApplyOrRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            ClassicWarlockDruid.ScheduleDemonTalentSync(GetUnitOwner());
        }

        // GetUnitOwner, not GetTarget: on the heartbeat there is no current aura application
        void HandleHeartbeat()
        {
            ClassicWarlockDruid.ScheduleDemonTalentSync(GetUnitOwner());
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApplyOrRemove, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
            AfterEffectRemove.Add(new(HandleApplyOrRemove, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
            OnHeartbeat.Add(new(HandleHeartbeat));
        }
    }

    // 688 - Summon Imp, 691 - Summon Felhunter, 697 - Summon Voidwalker, 712 - Summon Succubus, 713 - Summon Incubus, 427733 - Summon Felguard
    // Summoning a demon cancels the Demonic Sacrifice buff (vanilla, VMaNGOS Unit::SummonPet / patch 1.12).
    // 425464 - Demonic Pact: "Your Demonic Sacrifice effect is no longer cancelled by summoning a different Demon pet. Resummoning the
    // sacrificed pet will still cancel the effect."
    [Script]
    class classic_talent_warl_summon_demon : SpellScript
    {
        void HandleAfterCast()
        {
            Unit caster = GetCaster().ToUnit();
            DemonKind summoned = ClassicWarlockDruid.GetDemonKind(GetEffectInfo(0).MiscValue);
            int summonedBuff = ClassicWarlockDruid.GetSacrificeBuff(summoned);
            bool demonicPact = caster.HasAura(SpellIds.DemonicPact);

            foreach (int buff in ClassicWarlockDruid.SacrificeBuffs)
            {
                if (demonicPact && buff != summonedBuff)
                    continue;
                caster.RemoveAurasDueToSpell(buff);
            }

            ClassicWarlockDruid.ScheduleDemonTalentSync(caster, 500);
        }

        public override void Register()
        {
            AfterCast.Add(new(HandleAfterCast));
        }
    }

    // 18788 - Demonic Sacrifice: "Sacrifices your summoned demon to grant you an effect that lasts 30 min." The client spell only kills the
    // demon (instakill on the pet); the buff of the sacrificed demon (VMaNGOS WarlockDemonicSacrificeScript): Imp 18789 Burning Shadow,
    // Voidwalker 18790 Fel Stamina, Succubus/Incubus 18791 Touch of Fire, Felhunter 18792 Fel Energy.
    [Script]
    class classic_talent_warl_demonic_sacrifice : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(ClassicWarlockDruid.SacrificeBuffs);
        }

        void HandleHit(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            Unit demon = GetHitUnit();
            if (demon == null || demon.GetOwnerGUID() != caster.GetGUID())
                return;

            int buff = ClassicWarlockDruid.GetSacrificeBuff(ClassicWarlockDruid.GetDemonKind(demon.GetEntry()));
            if (buff == 0)
                return;

            foreach (int other in ClassicWarlockDruid.SacrificeBuffs)
                if (other != buff)
                    caster.RemoveAurasDueToSpell(other);

            caster.CastSpell(caster, buff, new CastSpellExtraArgs(TriggerCastFlags.FullMask));
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleHit, 0, SpellEffectName.Instakill));
        }
    }

    // 17962, 18930, 18931, 18932, 1293817, 1293818 - Conflagrate: "Ignites a target that is already afflicted by your Immolate spell, dealing
    // Fire damage and consuming your Immolate effect." (the consume was missing: the Immolate DoT and its hidden marker 1282590 are removed)
    // 426316 - Shadow and Flame: "Hitting an enemy with Conflagrate increases all Shadow damage you deal by $m3% for $1293816d [...]
    // Conflagrate has a $m2% chance not to consume Immolate".
    [Script]
    class classic_talent_warl_conflagrate : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ShadowAndFlameShadow);
        }

        void HandleHit(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            Unit target = GetHitUnit();
            if (target == null)
                return;

            AuraEffect keepChance = caster.GetAuraEffect(SpellIds.ShadowAndFlame, 1);
            AuraEffect shadowBonus = caster.GetAuraEffect(SpellIds.ShadowAndFlame, 2);
            if (shadowBonus != null)
                caster.CastSpell(caster, SpellIds.ShadowAndFlameShadow, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.BasePoint0, shadowBonus.GetAmount()));

            if (keepChance != null && RandomHelper.randChance(keepChance.GetAmount()))
                return;

            // Immolate: warlock family flag 0x4
            AuraEffect immolate = target.GetAuraEffect(AuraType.PeriodicDamage, SpellFamilyNames.Warlock, new FlagArray128(0x4, 0, 0, 0), caster.GetGUID());
            immolate?.GetBase().Remove();
            target.RemoveAurasDueToSpell(SpellIds.ImmolateMarker, caster.GetGUID());
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleHit, 0, SpellEffectName.SchoolDamage));
        }
    }

    // 17877, 18867, 18868, 18869, 18870, 18871 - Shadowburn
    // 426316 - Shadow and Flame: "[...] hitting an enemy with Shadowburn increases all Fire damage you deal by $m4% for $426311d. [...]
    // Shadowburn has a $m2% chance to instantly refund a Soul Shard." (Shadowburn costs a Soul Shard)
    [Script]
    class classic_talent_warl_shadowburn : SpellScript
    {
        bool _shardRefunded;

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ShadowAndFlameFire);
        }

        void HandleHit(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            AuraEffect fireBonus = caster.GetAuraEffect(SpellIds.ShadowAndFlame, 3);
            if (fireBonus != null)
                caster.CastSpell(caster, SpellIds.ShadowAndFlameFire, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.BasePoint0, fireBonus.GetAmount()));

            if (_shardRefunded || GetSpell().IsTriggered())
                return;

            AuraEffect refundChance = caster.GetAuraEffect(SpellIds.ShadowAndFlame, 1);
            if (refundChance == null || !RandomHelper.randChance(refundChance.GetAmount()))
                return;

            Player player = caster.ToPlayer();
            if (player != null)
            {
                player.AddItem(Misc.ItemSoulShard, 1);
                _shardRefunded = true;
            }
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleHit, 1, SpellEffectName.SchoolDamage));
        }
    }

    // 1243338 - Bane of Havoc (warlock side, proc on the warlock's damage). Deals $1225228s1% of the damage done to other targets to the
    // cursed target as 1243339.
    [Script]
    class classic_talent_warl_bane_of_havoc_tracker : AuraScript
    {
        ObjectGuid _havocTarget;

        public ObjectGuid GetHavocTarget() { return _havocTarget; }
        public void SetHavocTarget(ObjectGuid guid) { _havocTarget = guid; }

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.BaneOfHavoc, SpellIds.BaneOfHavocDamage);
        }

        Unit GetHavocUnit()
        {
            if (_havocTarget.IsEmpty())
                return null;

            Unit warlock = GetUnitOwner();
            Unit havoc = ObjAccessor.GetUnit(warlock, _havocTarget);
            if (havoc == null || !havoc.IsAlive() || !havoc.HasAura(SpellIds.BaneOfHavoc, warlock.GetGUID()))
                return null;
            return havoc;
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            if (damageInfo == null || damageInfo.GetDamage() == 0)
                return false;

            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            if (spellInfo != null && spellInfo.Id == SpellIds.BaneOfHavocDamage)
                return false;

            Unit victim = eventInfo.GetActionTarget();
            if (victim == null || victim == GetTarget() || victim.GetGUID() == _havocTarget)
                return false;

            return GetHavocUnit() != null;
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            Unit havoc = GetHavocUnit();
            if (havoc == null)
                return;

            int pct = aurEff.GetAmount();
            AuraEffect bane = havoc.GetAuraEffect(SpellIds.BaneOfHavoc, 0, GetTarget().GetGUID());
            if (bane != null)
                pct = bane.GetAmount();

            int damage = MathFunctions.CalculatePct(eventInfo.GetDamageInfo().GetDamage(), pct);
            if (damage <= 0)
                return;

            GetTarget().CastSpell(havoc, SpellIds.BaneOfHavocDamage, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, damage));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 1225228 - Bane of Havoc: "Afflicts the target for $d, causing $s1% of all damage done by the Warlock to other targets to also be dealt
    // to the cursed target. Bane of Havoc is limited to 1 target". The curse (dummy) puts the tracker 1243338 on the warlock and removes the
    // warlock's previous Bane of Havoc.
    [Script]
    class classic_talent_warl_bane_of_havoc : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.BaneOfHavocTracker);
        }

        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Unit caster = GetCaster();
            if (caster == null)
                return;

            Unit target = GetTarget();
            Aura tracker = caster.GetAura(SpellIds.BaneOfHavocTracker, caster.GetGUID());
            if (tracker == null)
            {
                caster.CastSpell(caster, SpellIds.BaneOfHavocTracker, new CastSpellExtraArgs(TriggerCastFlags.FullMask));
                tracker = caster.GetAura(SpellIds.BaneOfHavocTracker, caster.GetGUID());
            }

            var script = tracker?.GetScript<classic_talent_warl_bane_of_havoc_tracker>();
            if (script == null)
                return;

            ObjectGuid previous = script.GetHavocTarget();
            script.SetHavocTarget(target.GetGUID());

            if (!previous.IsEmpty() && previous != target.GetGUID())
                ObjAccessor.GetUnit(caster, previous)?.RemoveAurasDueToSpell(SpellIds.BaneOfHavoc, caster.GetGUID());
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Unit caster = GetCaster();
            if (caster == null)
                return;

            Aura tracker = caster.GetAura(SpellIds.BaneOfHavocTracker, caster.GetGUID());
            if (tracker == null)
                return;

            var script = tracker.GetScript<classic_talent_warl_bane_of_havoc_tracker>();
            if (script == null || script.GetHavocTarget() == GetTarget().GetGUID())
                tracker.Remove();
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
            AfterEffectRemove.Add(new(HandleRemove, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
        }
    }

    // 6201, 6202, 5699, 11729, 11730 - Create Healthstone (client: script effect, nothing was created)
    // Vanilla (VMaNGOS WarlockCreateHealthstoneScript): the item depends on 18692 / 18693 Improved Healthstone ("Increases the amount of
    // Health restored by your Healthstone by $s1%"): rank 1 and rank 2 have their own, stronger healthstone items.
    [Script]
    class classic_talent_warl_create_healthstone : SpellScript
    {
        static readonly int[,] Items =
        {
            { 5512, 19004, 19005 },     // Minor Healthstone
            { 5511, 19006, 19007 },     // Lesser Healthstone
            { 5509, 19008, 19009 },     // Healthstone
            { 5510, 19010, 19011 },     // Greater Healthstone
            { 9421, 19012, 19013 },     // Major Healthstone
        };

        static int GetItemId(Unit caster, int spellId)
        {
            int rank = 0;
            if (caster.HasAura(SpellIds.ImprovedHealthstoneR2))
                rank = 2;
            else
            {
                AuraEffect rank1 = caster.GetAuraEffect(SpellIds.ImprovedHealthstoneR1, 0);
                if (rank1 != null)
                    rank = rank1.GetAmount() >= 20 ? 2 : 1;     // single trait spell: the curve gives the rank 2 value
            }

            switch (spellId)
            {
                case 6201: return Items[0, rank];
                case 6202: return Items[1, rank];
                case 5699: return Items[2, rank];
                case 11729: return Items[3, rank];
                case 11730: return Items[4, rank];
                default: return 0;
            }
        }

        SpellCastResult CheckCast()
        {
            Player player = GetCaster().ToPlayer();
            if (player == null)
                return SpellCastResult.SpellCastOk;

            int itemId = GetItemId(player, GetSpellInfo().Id);
            if (itemId == 0)
                return SpellCastResult.SpellCastOk;

            InventoryResult msg = player.CanStoreNewItem(ItemPos.Undefined, out _, itemId, 1);
            if (msg != InventoryResult.Ok)
            {
                player.SendEquipError(msg, null, null, itemId);
                return SpellCastResult.DontReport;
            }

            return SpellCastResult.SpellCastOk;
        }

        void HandleScript(int effIndex)
        {
            Unit target = GetHitUnit();
            if (target == null || !target.IsPlayer())
                return;

            int itemId = GetItemId(target, GetSpellInfo().Id);
            if (itemId != 0)
                CreateItem(itemId, ItemContext.None);
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            OnEffectHitTarget.Add(new(HandleScript, 0, SpellEffectName.ScriptEffect));
        }
    }

    /*######
    ## Druid
    ######*/

    // Classic 1.60 (client 70374) Furor, second part: "Shapeshifting into Cat Form restores ${100*($m2/100)}% of the Energy you had when last
    // in Cat Form, plus ${10*($m2/100)} Energy per sec spent outside Cat Form, up to ${100*($m2/100)} Energy. Bear Form and Dire Bear Form
    // forfeit this Energy and halt its restoration while active." ($m2 = effect 1 of 17056, 20% per rank)
    static class ClassicFurorCat
    {
        class State
        {
            public int Energy;                  // energy when the druid last left Cat Form (0 after a bear form)
            public DateTime CountingSince;      // start of the time that restores energy (left Cat Form, or left a bear form)
            public bool Counting;               // false while in a bear form
        }

        static readonly Dictionary<ObjectGuid, State> States = new();
        static readonly object StatesLock = new();

        public static void LeftCatForm(Unit druid)
        {
            lock (StatesLock)
                States[druid.GetGUID()] = new State { Energy = druid.GetPower(PowerType.Energy), CountingSince = DateTime.UtcNow, Counting = true };
        }

        public static void EnteredBearForm(Unit druid)
        {
            lock (StatesLock)
                if (States.ContainsKey(druid.GetGUID()))
                    States[druid.GetGUID()] = new State();      // energy forfeited, restoration halted
        }

        public static void LeftBearForm(Unit druid)
        {
            lock (StatesLock)
            {
                if (States.TryGetValue(druid.GetGUID(), out State state))
                {
                    state.CountingSince = DateTime.UtcNow;
                    state.Counting = true;
                }
            }
        }

        public static void EnteredCatForm(Unit druid)
        {
            AuraEffect furor = druid.GetAuraEffect(SpellIds.Furor, 1);
            State state;
            lock (StatesLock)
            {
                if (!States.TryGetValue(druid.GetGUID(), out State stored))
                    return;
                state = new State { Energy = stored.Energy, CountingSince = stored.CountingSince, Counting = stored.Counting };
            }
            if (furor == null || furor.GetAmount() <= 0)
                return;

            float factor = furor.GetAmount() / 100.0f;
            float energy = state.Energy * factor;
            if (state.Counting)
                energy += 10.0f * factor * (float)(DateTime.UtcNow - state.CountingSince).TotalSeconds;
            energy = Math.Min(energy, 100.0f * factor);
            if ((int)energy > druid.GetPower(PowerType.Energy))
                druid.SetPower(PowerType.Energy, (int)energy);
        }
    }

    // 768 - Cat Form, 5487 - Bear Form, 9634 - Dire Bear Form, 24858 - Moonkin Form
    // 16929 - Thick Hide: base armor while in these forms (UpdateThickHide).
    // 17056 - Furor: "Gives you a $m1% chance to gain 10 Rage when shapeshifting into Bear Form or Dire Bear Form." (17057: 10 rage), and the
    // Cat Form energy (ClassicFurorCat)
    [Script]
    class classic_talent_dru_form_talents : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ThickHideArmor, SpellIds.FurorRage);
        }

        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Unit druid = GetTarget();
            ClassicWarlockDruid.UpdateThickHide(druid);

            ShapeShiftForm form = (ShapeShiftForm)aurEff.GetMiscValue();
            if (form == ShapeShiftForm.BearForm || form == ShapeShiftForm.DireBearForm)
            {
                ClassicFurorCat.EnteredBearForm(druid);
                AuraEffect furor = druid.GetAuraEffect(SpellIds.Furor, 0);
                if (furor != null && RandomHelper.randChance(furor.GetAmount()))
                    druid.CastSpell(druid, SpellIds.FurorRage, new CastSpellExtraArgs(TriggerCastFlags.FullMask));
            }
            else if (form == ShapeShiftForm.CatForm)
                ClassicFurorCat.EnteredCatForm(druid);
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Unit druid = GetTarget();
            druid.RemoveAurasDueToSpell(SpellIds.ThickHideArmor);

            ShapeShiftForm form = (ShapeShiftForm)aurEff.GetMiscValue();
            if (form == ShapeShiftForm.CatForm)
                ClassicFurorCat.LeftCatForm(druid);
            else if (form == ShapeShiftForm.BearForm || form == ShapeShiftForm.DireBearForm)
                ClassicFurorCat.LeftBearForm(druid);
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 0, AuraType.ModShapeshift, AuraEffectHandleModes.Real));
            AfterEffectRemove.Add(new(HandleRemove, 0, AuraType.ModShapeshift, AuraEffectHandleModes.Real));
        }
    }

    // 16929 - Thick Hide: keeps the form armor up to date (level up, defense skill, talent learned or removed while shapeshifted)
    [Script]
    class classic_talent_dru_thick_hide : AuraScript
    {
        void Refresh()
        {
            // GetUnitOwner, not GetTarget: on the heartbeat there is no current aura application
            Unit druid = GetUnitOwner();
            if (druid == null || !druid.IsInWorld)
                return;

            druid.m_Events.AddEventAtOffset(() => ClassicWarlockDruid.UpdateThickHide(druid), TimeSpan.Zero);
        }

        void HandleApplyOrRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Refresh();
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApplyOrRemove, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
            AfterEffectRemove.Add(new(HandleApplyOrRemove, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
            OnHeartbeat.Add(new(Refresh));
        }
    }

    // 16958 - Blood Frenzy (Primal Fury in vanilla): "Gives you a $m1% chance to gain an additional $/10;16959s1 Rage any time you get a critical
    // strike while in Bear Form or Dire Bear Form. In addition, your non-periodic critical strikes from Cat Form abilities that generate Combo
    // Points have a $m2% chance to add an additional Combo Point." (proc: melee swing + melee ability)
    [Script]
    class classic_talent_dru_blood_frenzy : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.BloodFrenzyRage);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            if (!eventInfo.GetHitMask().HasAnyFlag(ProcFlagsHit.Critical) || eventInfo.GetActionTarget() == null)
                return false;

            switch (GetTarget().GetShapeshiftForm())
            {
                case ShapeShiftForm.BearForm:
                case ShapeShiftForm.DireBearForm:
                    return true;
                case ShapeShiftForm.CatForm:
                {
                    SpellInfo spellInfo = eventInfo.GetSpellInfo();
                    return GetTarget().IsPlayer() && spellInfo != null && !eventInfo.GetTypeMask().HasAnyFlag(ProcFlags.DealHarmfulPeriodic)
                        && ClassicSpells.IsComboPointBuilder(spellInfo);
                }
                default:
                    return false;
            }
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            Unit druid = GetTarget();
            if (druid.GetShapeshiftForm() == ShapeShiftForm.CatForm)
            {
                AuraEffect comboChance = GetEffect(1);
                if (comboChance == null || !RandomHelper.randChance(comboChance.GetAmount()))
                    return;

                // combo points belong to one target (game rule 112, Spell::EffectEnergize)
                Unit target = eventInfo.GetActionTarget();
                if (druid.GetClassicComboTarget() != target.GetGUID())
                {
                    druid.SetPower(PowerType.ComboPoints, 0);
                    druid.SetClassicComboTarget(target.GetGUID());
                }
                druid.SetPower(PowerType.ComboPoints, druid.GetPower(PowerType.ComboPoints) + 1);
                return;
            }

            if (RandomHelper.randChance(aurEff.GetAmount()))
                druid.CastSpell(druid, SpellIds.BloodFrenzyRage, new CastSpellExtraArgs(aurEff));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 408248 - Eclipse: "Your Wrath spell reduces the cast time of your next $s3 Starfire spells by ${$m2/-1000}.2 sec. Stores up to $408255u
    // charges. Lasts $408255d." Each Wrath hit adds $s3 stacks of 408255 (max stack 4, the buff effect 0 is the per charge cast time change
    // in ms, multiplied by the stacks: "Your next $m2 Starfire casts have ${$w1/$w2/1000}.1 sec reduced cast time").
    [Script]
    class classic_talent_dru_eclipse : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.EclipseBuff);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            // Wrath: druid family flag 0x1
            return spellInfo != null && spellInfo.SpellFamilyName == SpellFamilyNames.Druid && (spellInfo.SpellFamilyFlags[0] & 0x1) != 0;
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            Unit druid = GetTarget();
            SpellInfo buffInfo = SpellMgr.GetSpellInfo(SpellIds.EclipseBuff, GetCastDifficulty());

            AuraEffect reductionEffect = GetEffect(1);
            AuraEffect chargesEffect = GetEffect(2);
            int charges = chargesEffect != null && chargesEffect.GetAmount() > 0 ? chargesEffect.GetAmount() : 2;

            // per charge cast time change in ms (negative); without a usable talent value keep the client buff value
            int perCharge = buffInfo.GetEffect(0).CalcValue(druid);
            if (reductionEffect != null && Math.Abs(reductionEffect.GetAmount()) >= 100)
                perCharge = -Math.Abs(reductionEffect.GetAmount());

            Aura buff = druid.GetAura(SpellIds.EclipseBuff, druid.GetGUID());
            int stacks = buff != null ? buff.GetStackAmount() : 0;
            int maxStacks = buffInfo.StackAmount != 0 ? buffInfo.StackAmount : 4;
            int newStacks = Math.Min(maxStacks, stacks + charges);

            if (buff == null)
            {
                druid.CastSpell(druid, SpellIds.EclipseBuff, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, perCharge));
                buff = druid.GetAura(SpellIds.EclipseBuff, druid.GetGUID());
                if (buff == null)
                    return;
            }

            if (buff.GetStackAmount() != newStacks)
                buff.SetStackAmount((byte)newStacks);
            buff.RefreshDuration();
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 408255 - Eclipse (buff): the client data has a proc charge on any harmful spell; the Starfire script consumes the stacks instead
    [Script]
    class classic_talent_dru_eclipse_buff : AuraScript
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

    // 2912, 8949, 8950, 8951, 9875, 9876, 25298 - Starfire: one 408255 Eclipse charge reduces the cast time
    [Script]
    class classic_talent_dru_eclipse_starfire : SpellScript
    {
        bool _usesCharge;

        public override Milliseconds CalcCastTime(Milliseconds castTime)
        {
            Unit caster = GetCaster().ToUnit();
            AuraEffect buff = caster?.GetAuraEffect(SpellIds.EclipseBuff, 0, caster.GetGUID());
            if (buff != null)
            {
                _usesCharge = true;
                castTime = (Milliseconds)Math.Max(0, (int)castTime + buff.GetBaseAmount());
            }
            return castTime;
        }

        void HandleAfterCast()
        {
            if (!_usesCharge)
                return;

            Unit caster = GetCaster().ToUnit();
            caster?.GetAura(SpellIds.EclipseBuff, caster.GetGUID())?.ModStackAmount(-1);
        }

        public override void Register()
        {
            AfterCast.Add(new(HandleAfterCast));
        }
    }

    // 408258 - Dreamstate: "Your damaging non-periodic spell critical strikes grant you $m1% of your mana regeneration while casting for
    // $408261d." (408261: SPELL_AURA_MOD_MANA_REGEN_INTERRUPT)
    [Script]
    class classic_talent_dru_dreamstate : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.DreamstateBuff);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            return eventInfo.GetSpellInfo() != null && damageInfo != null && damageInfo.GetDamage() != 0 && eventInfo.GetHitMask().HasAnyFlag(ProcFlagsHit.Critical)
                && !eventInfo.GetTypeMask().HasAnyFlag(ProcFlags.DealHarmfulPeriodic);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            GetTarget().CastSpell(GetTarget(), SpellIds.DreamstateBuff, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, aurEff.GetAmount()));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 5217 - Tiger's Fury
    // 417046 - King of the Jungle: "Tiger's Fury now instantly grants you $s1 Energy." (417045: energize energy)
    [Script]
    class classic_talent_dru_king_of_the_jungle : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.KingOfTheJungleEnergize);
        }

        void HandleAfterCast()
        {
            Unit caster = GetCaster().ToUnit();
            AuraEffect talent = caster?.GetAuraEffect(SpellIds.KingOfTheJungle, 0);
            if (talent != null && talent.GetAmount() > 0)
                caster.CastSpell(caster, SpellIds.KingOfTheJungleEnergize, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.BasePoint0, talent.GetAmount()));
        }

        public override void Register()
        {
            AfterCast.Add(new(HandleAfterCast));
        }
    }

    // 1223246 - Rend and Tear (druid): "Increases damage done by your melee abilities on Bleeding targets by $m1%."
    // 426301 - Grimoire of Synergy (warlock): "Damage done by you or your summoned demon has a $h% chance to increase the damage done by the
    // other by $426303s1% for $426303d." The talent spell is an area aura owner effect on the pet, so the warlock side is checked by spell.
    [Script]
    class classic_unit_talent_warlock_druid : UnitScript
    {
        public classic_unit_talent_warlock_druid() : base("classic_unit_talent_warlock_druid") { }

        public override void ModifySpellDamageTaken(Unit target, Unit attacker, ref int damage, SpellInfo spellInfo)
        {
            if (target == null || attacker == null || spellInfo == null || damage <= 0 || spellInfo.DmgClass != SpellDmgClass.Melee || !attacker.IsPlayer())
                return;

            AuraEffect talent = attacker.GetAuraEffect(SpellIds.RendAndTear, 0);
            if (talent == null || talent.GetAmount() <= 0)
                return;

            if (target.HasAuraWithMechanic(1ul << (int)Mechanics.Bleed))
                MathFunctions.AddPct(ref damage, talent.GetAmount());
        }

        static bool HasGrimoireOfSynergy(Player warlock)
        {
            return warlock.HasAura(SpellIds.GrimoireOfSynergy) || warlock.HasSpell(SpellIds.GrimoireOfSynergy);
        }

        public override void OnDamage(Unit attacker, Unit victim, ref int damage)
        {
            if (attacker == null || victim == null || damage == 0 || attacker == victim)
                return;

            Unit other = null;
            Player warlock = attacker.ToPlayer();
            if (warlock != null)
            {
                if (warlock.GetClass() != Class.Warlock || !HasGrimoireOfSynergy(warlock))
                    return;
                other = ClassicWarlockDruid.GetActiveDemon(warlock);
            }
            else if (attacker.IsPet())
            {
                warlock = attacker.GetCharmerOrOwnerPlayerOrPlayerItself();
                if (warlock == null || warlock == attacker || warlock.GetClass() != Class.Warlock || warlock.GetPet() != attacker
                    || ClassicWarlockDruid.GetDemonKind(attacker.GetEntry()) == DemonKind.None || !HasGrimoireOfSynergy(warlock))
                    return;
                other = warlock;
            }

            if (other == null || !other.IsAlive() || other == victim)
                return;

            SpellInfo talentInfo = SpellMgr.GetSpellInfo(SpellIds.GrimoireOfSynergy, Difficulty.None);
            float chance = talentInfo != null && talentInfo.ProcChance != 0 ? talentInfo.ProcChance : 10.0f;
            if (!RandomHelper.randChance(chance))
                return;

            attacker.CastSpell(other, SpellIds.GrimoireOfSynergyBuff, new CastSpellExtraArgs(TriggerCastFlags.FullMask));
        }
    }
}
