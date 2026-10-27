// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Framework.Dynamic;
using Game.Entities;
using Game.Maps;
using Game.Scripting;
using Game.Spells;
using System;
using System.Collections.Generic;
using static Global;

// Classic 1.60 (WoW Forever) mage and shaman talents, ported from the reference TrinityCore
// (src/server/scripts/Custom/Classic/world/talents/classic_talents_mage_shaman.cpp). In the client data these talents are dummy auras
// (most with proc data) and have no server side behaviour; the scripts implement the Forever tooltips. Rank values come from the talent's
// aura amounts (the core applies the trait rank curve), never from hardcoded ranks.
namespace Scripts.Spells.Classic.MageShaman
{
    struct SpellIds
    {
        public const int IgniteDot = 412538;                    // periodic dummy, 2 s ticks for 4 s (1.60 replacement of 12654)
        public const int Shatter = 11170;
        public const int ImprovedBlizzard = 11185;
        public const int Chilled = 12484;                       // Improved Blizzard chill, 1.5 s
        public const int ImprovedCounterspell = 11255;
        public const int CounterspellSilenced = 18469;
        public const int HeatingUpBuff = 400625;                // -25% Pyroblast cast time per stack, 3 stacks, 20 s, 1 charge
        public const int FingersOfFrostTalent = 400647;         // effect 0: charges, effect 1: proc chance
        public const int FingersOfFrostIgnore = 400669;         // ability ignore aura state (frozen) for the spells that need it
        public const int FingersOfFrostStacks = 400670;         // 2 stacks, one used per spell cast

        public const int FlurryBuff = 16257;
        public const int ElementalDevastationBuff = 30165;
        public const int MaelstromWeaponTalent = 408498;        // effect 1: proc chance, effect 2: max stacks
        public const int MaelstromWeaponBuff = 408505;
        public const int MaelstromReady = 408501;
        public const int LightningBoltMaelstrom = 1291078;      // lets Lightning Bolt hits activate Maelstrom Weapon (effect 0: chance)
        public const int EarthboundTalent = 1222988;
        public const int EarthboundRoot = 1238289;
        public const int ImprovedStormstrike = 1223031;         // effect 0: buff chance, effect 2: cooldown reset chance
        public const int ImprovedStormstrikeBuff = 1238931;     // 50% mana regeneration while casting, 15 s
        public const int Stormstrike = 17364;
        public const int AncestralGuidance = 409324;
        public const int AncestralGuidanceHeal = 409333;
        public const int AncestralGuidanceDamage = 409337;
    }

    static class ClassicMage
    {
        // Mage "Chill effects" (Frostbolt, Cone of Cold, Frostfire Bolt, Blast Wave...): mage spells with a snare. Triggered chills do not
        // proc (Frost Armor / Ice Armor chill); the Improved Blizzard chill rolls directly.
        public static bool IsChillEffect(SpellInfo spellInfo)
        {
            return spellInfo != null && spellInfo.SpellFamilyName == SpellFamilyNames.Mage && spellInfo.HasAura(AuraType.ModDecreaseSpeed);
        }

        // 400647 - Fingers of Frost: a Chill effect has effect 1 % chance to grant effect 0 charges of Fingers of Frost for 15 s
        public static void TryGrantFingersOfFrost(Unit mage)
        {
            Aura talent = mage.GetAura(SpellIds.FingersOfFrostTalent);
            if (talent == null)
                return;

            AuraEffect charges = talent.GetEffect(0);
            AuraEffect chance = talent.GetEffect(1);
            if (charges == null || chance == null || !RandomHelper.randChance(chance.GetAmount()))
                return;

            mage.CastSpell(mage, SpellIds.FingersOfFrostIgnore, true);
            mage.CastSpell(mage, SpellIds.FingersOfFrostStacks, new CastSpellExtraArgs(TriggerCastFlags.FullMask)
                .AddSpellMod(SpellValueMod.AuraStack, Math.Max(1, charges.GetAmount())));
        }
    }

    // 11119 - Ignite: "Your critical strikes from Fire damage spells cause the target to burn for an additional 40% of your spell's
    // damage over 4 sec." Vanilla 12654 is not in the 1.60 client; 412538 (Ignite, periodic dummy 2 s / 4 s) is the DoT. The
    // remaining damage of a running Ignite rolls into the new one (spell_proc: fire, harmful spell, damage, hit, critical).
    [Script]
    class classic_spell_mage_ignite : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellEffect((SpellIds.IgniteDot, 0));
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            return eventInfo.GetActionTarget() != null && eventInfo.GetDamageInfo() != null && eventInfo.GetDamageInfo().GetDamage() != 0
                && eventInfo.GetDamageInfo().GetSchoolMask().HasAnyFlag(SpellSchoolMask.Fire);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            Unit mage = eventInfo.GetActor();
            Unit target = eventInfo.GetActionTarget();
            SpellInfo dot = SpellMgr.GetSpellInfo(SpellIds.IgniteDot, GetCastDifficulty());
            int period = Math.Max(1, (int)dot.GetEffect(0).ApplyAuraPeriod);
            int totalTicks = Math.Max(1, dot.GetMaxDuration() / period);

            long amount = MathFunctions.CalculatePct((long)eventInfo.GetDamageInfo().GetDamage(), aurEff.GetAmount());
            AuraEffect existing = target.GetAuraEffect(SpellIds.IgniteDot, 0, mage.GetGUID());
            if (existing != null)
                amount += (long)existing.GetAmount() * existing.GetRemainingTicks();

            amount /= totalTicks;
            if (amount < 1)
                return;

            mage.CastSpell(target, SpellIds.IgniteDot, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, (int)amount));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 412538 - Ignite (DoT): every tick deals its amount as Fire damage (already modified by the critical spell, no bonuses again)
    [Script]
    class classic_spell_mage_ignite_dot : AuraScript
    {
        void HandleTick(AuraEffect aurEff)
        {
            Unit caster = GetCaster();
            Unit target = GetTarget();
            if (caster == null || !target.IsAlive())
                return;

            int damage = aurEff.GetAmount();
            if (damage <= 0)
                return;

            ClassicSpells.DealPeriodicSpellDamage(caster, target, GetSpellInfo(), GetAura(), damage);
        }

        public override void Register()
        {
            OnEffectPeriodic.Add(new(HandleTick, 0, AuraType.PeriodicDummy));
        }
    }

    // 11170 - Shatter: "Increases the critical strike chance of all your spells against Frozen targets by 1%" (rank amount).
    // 400647 - Fingers of Frost: "treats your next 2 spells cast as if the target were Frozen": every cast mage damage spell uses
    // one charge of 400670 and counts as hitting a Frozen target (for Shatter). Bound to all player mage damage spells.
    [Script]
    class classic_spell_mage_frozen_target : SpellScript
    {
        bool _fingersOfFrost;

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.FingersOfFrostStacks);
        }

        void HandleCast()
        {
            // launch (critical roll) happens after OnCast, so the charge is used here and remembered for this cast
            if (GetSpell().IsTriggered())
                return;

            Aura fingers = GetCaster().ToUnit()?.GetAura(SpellIds.FingersOfFrostStacks);
            if (fingers != null)
            {
                _fingersOfFrost = true;
                fingers.ModStackAmount(-1);
            }
        }

        void CalcCritChance(Unit victim, ref float critChance)
        {
            Unit caster = GetCaster().ToUnit();
            AuraEffect shatter = caster?.GetAuraEffect(SpellIds.Shatter, 0);
            if (shatter == null)
                return;

            bool frozen = _fingersOfFrost
                || (GetSpell().IsTriggered() && caster.HasAura(SpellIds.FingersOfFrostStacks))
                || victim.HasAuraState(AuraStateType.Frozen, GetSpellInfo(), caster);
            if (frozen)
                critChance += shatter.GetAmount();
        }

        public override void Register()
        {
            OnCast.Add(new(HandleCast));
            OnCalcCritChance.Add(new(CalcCritChance));
        }
    }

    // 400670 - Fingers of Frost (charges): the aura state ignore buff 400669 ends with the last charge
    [Script]
    class classic_spell_mage_fingers_of_frost_charges : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.FingersOfFrostIgnore);
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            GetTarget().RemoveAurasDueToSpell(SpellIds.FingersOfFrostIgnore);
        }

        public override void Register()
        {
            AfterEffectRemove.Add(new(HandleRemove, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
        }
    }

    // 400647 - Fingers of Frost: "Gives your Chill effects a 15% chance to grant you the Fingers of Frost effect, which treats your
    // next 2 spells cast as if the target were Frozen. Lasts 15 sec." (spell_proc: mage harmful spell hits; the Blizzard chill is
    // cast triggered and rolls from classic_spell_mage_improved_blizzard)
    [Script]
    class classic_spell_mage_fingers_of_frost : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.FingersOfFrostIgnore, SpellIds.FingersOfFrostStacks)
                && ValidateSpellEffect((SpellIds.FingersOfFrostTalent, 1));
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            return ClassicMage.IsChillEffect(eventInfo.GetSpellInfo());
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            ClassicMage.TryGrantFingersOfFrost(GetTarget());
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 11185 - Improved Blizzard: "Adds a Chill effect to your Blizzard spell. This effect lowers the target's movement speed by 40%
    // for 1.5 sec." Blizzard ticks through classic_spell_ground_area_damage; this second script on the same tick chills the enemies
    // inside the Blizzard with 12484 (talent amount as the snare), which is a Chill effect for Fingers of Frost.
    [Script] // 10, 6141, 8427, 10185-10187 - Blizzard
    class classic_spell_mage_improved_blizzard : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.Chilled, SpellIds.ImprovedBlizzard);
        }

        void HandleTick(AuraEffect aurEff)
        {
            Unit caster = GetCaster();
            if (caster == null)
                return;

            AuraEffect talent = caster.GetAuraEffect(SpellIds.ImprovedBlizzard, 0);
            if (talent == null || talent.GetAmount() >= 0)
                return;

            bool chilled = false;
            foreach (Unit target in ClassicSpells.GetGroundTargets(caster, GetSpellInfo()))
            {
                caster.CastSpell(target, SpellIds.Chilled, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.BasePoint0, talent.GetAmount()));
                chilled = true;
            }

            if (chilled)
                ClassicMage.TryGrantFingersOfFrost(caster);
        }

        public override void Register()
        {
            OnEffectPeriodic.Add(new(HandleTick, 2, AuraType.PeriodicDummy));
        }
    }

    // 2139 - Counterspell, with 11255 Improved Counterspell: "Your Counterspell also Silences the target for 4 sec." (18469, the
    // talent amount is the duration in ms; vanilla silences on every Counterspell hit)
    [Script]
    class classic_spell_mage_counterspell : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.CounterspellSilenced);
        }

        void HandleHit(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            Unit target = GetHitUnit();
            AuraEffect talent = caster?.GetAuraEffect(SpellIds.ImprovedCounterspell, 0);
            if (target == null || talent == null)
                return;

            CastSpellExtraArgs args = new(TriggerCastFlags.FullMask);
            if (talent.GetAmount() > 0)
                args.AddSpellMod(SpellValueMod.Duration, talent.GetAmount());
            caster.CastSpell(target, SpellIds.CounterspellSilenced, args);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleHit, 0, SpellEffectName.InterruptCast));
        }
    }

    // 400624 - Heating Up: "Non-periodic critical strikes with Fireball, Frostfire Bolt, Fire Blast, and Scorch reduce the cast time
    // of your next Pyroblast cast within 20 sec by 25%, stacking up to 3 times." Adds a stack of 400625 (talent amount = chance);
    // 400625 is used by the next Pyroblast (spell_proc: cast phase, requires its spell mod).
    [Script]
    class classic_spell_mage_heating_up : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.HeatingUpBuff);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            if (RandomHelper.randChance(aurEff.GetAmount()))
                GetTarget().CastSpell(GetTarget(), SpellIds.HeatingUpBuff, new CastSpellExtraArgs(aurEff));
        }

        public override void Register()
        {
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 16256 - Flurry: "Increases your attack speed by 1% for your next 3 swings after dealing a melee critical strike." 16257 (3
    // charges, used by swings) with the talent amount as its haste.
    [Script]
    class classic_spell_sha_flurry : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.FlurryBuff);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            CastSpellExtraArgs args = new(aurEff);
            if (aurEff.GetAmount() > 0)
                args.AddSpellMod(SpellValueMod.BasePoint0, aurEff.GetAmount());
            GetTarget().CastSpell(GetTarget(), SpellIds.FlurryBuff, args);
        }

        public override void Register()
        {
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 30160 - Elemental Devastation: "Your offensive spell critical strikes will increase your chance to get a critical strike with
    // melee attacks by X% for 10 sec." 30165 with the talent amount (its own 3% when the amount is not set).
    [Script]
    class classic_spell_sha_elemental_devastation : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ElementalDevastationBuff);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            CastSpellExtraArgs args = new(aurEff);
            if (aurEff.GetAmount() > 0)
                args.AddSpellMod(SpellValueMod.BasePoint0, aurEff.GetAmount());
            GetTarget().CastSpell(GetTarget(), SpellIds.ElementalDevastationBuff, args);
        }

        public override void Register()
        {
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 408438 - Lightning Overload: "Gives your Lightning Bolt and Chain Lightning spells a 30% chance to cast a second, similar spell
    // on the same target at no additional cost that causes half damage and no threat." The 1.60 overload spells (half damage,
    // SPELL_ATTR1_NO_THREAT) per rank; Chain Lightning only rolls on its primary target (the overload chains by itself).
    [Script]
    class classic_spell_sha_lightning_overload : AuraScript
    {
        static int GetOverloadSpell(int spellId)
        {
            switch (spellId)
            {
                // Lightning Bolt
                case 403: return 408439;
                case 529: return 408440;
                case 548: return 408441;
                case 915: return 408442;
                case 943: return 408443;
                case 6041: return 408472;
                case 10391: return 408473;
                case 10392: return 408474;
                case 15207: return 408475;
                case 15208: return 408477;
                // Chain Lightning
                case 421: return 408479;
                case 930: return 408481;
                case 2860: return 408482;
                case 10605: return 408484;
                default: return 0;
            }
        }

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(408439, 408440, 408441, 408442, 408443, 408472, 408473, 408474, 408475, 408477,
                408479, 408481, 408482, 408484);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            Spell spell = eventInfo.GetProcSpell();
            if (spellInfo == null || spell == null || spell.IsTriggered() || GetOverloadSpell(spellInfo.Id) == 0 || eventInfo.GetActionTarget() == null)
                return false;

            // one roll per cast: the explicit target of the bolt / chain
            Unit explicitTarget = spell.m_targets.GetUnitTarget();
            return explicitTarget == null || explicitTarget == eventInfo.GetActionTarget();
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            if (!RandomHelper.randChance(aurEff.GetAmount()))
                return;

            eventInfo.GetActor().CastSpell(eventInfo.GetActionTarget(), GetOverloadSpell(eventInfo.GetSpellInfo().Id), new CastSpellExtraArgs(aurEff));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 408498 - Maelstrom Weapon: "When you deal damage with a melee attack, you have a chance to reduce the cast time and Mana cost of
    // your next Lightning Bolt spell by 20%. Stacks up to 5 times. Lasts 30 sec." Effect 1 is the chance per melee hit, effect 2 the
    // stack count that shows "Maelstrom Ready!" (408501). With 1291078 Lightning Bolt hits can also add a stack (its amount = chance).
    // 408505 and 408501 are used by the next Lightning Bolt (spell_proc).
    [Script]
    class classic_spell_sha_maelstrom_weapon : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.MaelstromWeaponBuff, SpellIds.MaelstromReady)
                && ValidateSpellEffect((SpellIds.MaelstromWeaponTalent, 2));
        }

        static bool IsMelee(ProcEventInfo eventInfo)
        {
            return eventInfo.GetTypeMask().HasAnyFlag(ProcFlags.DealMeleeSwing | ProcFlags.DealMeleeAbility);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            if (damageInfo == null || damageInfo.GetDamage() == 0)
                return false;

            if (IsMelee(eventInfo))
                return true;

            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            return spellInfo != null && spellInfo.SpellFamilyName == SpellFamilyNames.Shaman && (spellInfo.SpellFamilyFlags[0] & 0x1) != 0
                && GetTarget().HasAura(SpellIds.LightningBoltMaelstrom);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            Unit shaman = GetTarget();
            int chance = GetAura().GetEffect(1).GetAmount();
            if (!IsMelee(eventInfo))
            {
                AuraEffect lightningBolt = shaman.GetAuraEffect(SpellIds.LightningBoltMaelstrom, 0);
                if (lightningBolt != null)
                    chance = lightningBolt.GetAmount();
            }

            if (!RandomHelper.randChance(chance))
                return;

            shaman.CastSpell(shaman, SpellIds.MaelstromWeaponBuff, true);

            int maxStacks = Math.Max(1, GetAura().GetEffect(2).GetAmount());
            Aura buff = shaman.GetAura(SpellIds.MaelstromWeaponBuff);
            if (buff != null && buff.GetStackAmount() >= maxStacks && !shaman.HasAura(SpellIds.MaelstromReady))
                shaman.CastSpell(shaman, SpellIds.MaelstromReady, true);
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
        }
    }

    // 408505 - Maelstrom Weapon (buff): "Maelstrom Ready!" (408501, no duration) ends with the stacks
    [Script]
    class classic_spell_sha_maelstrom_weapon_buff : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.MaelstromReady);
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            GetTarget().RemoveAurasDueToSpell(SpellIds.MaelstromReady);
        }

        public override void Register()
        {
            AfterEffectRemove.Add(new(HandleRemove, 0, AuraType.AddPctModifier, AuraEffectHandleModes.Real));
        }
    }

    // 1222988 - Earthbound: "Your Earthbind Totem Immobilizes nearby targets for 5 sec when cast." The Earthbind Totem (2630) gets
    // its passive 6474 when summoned; then it casts 1238289 (root, 10 yd around the totem).
    [Script]
    class classic_spell_sha_earthbind_totem_passive : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.EarthboundRoot);
        }

        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Unit totem = GetTarget();
            Unit owner = totem.GetOwner();
            if (owner == null)
                return;

            AuraEffect talent = owner.GetAuraEffect(SpellIds.EarthboundTalent, 0);
            if (talent == null || !RandomHelper.randChance(talent.GetAmount()))
                return;

            totem.CastSpell(totem, SpellIds.EarthboundRoot, true);
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 0, AuraType.PeriodicTriggerSpell, AuraEffectHandleModes.Real));
        }
    }

    // 1223031 - Improved Stormstrike (Stormstrike part): "When you Stormstrike, you have a 20% chance to gain 50% mana regeneration
    // while casting spells for 15 sec" (1238931, talent effect 0 = chance)
    [Script]
    class classic_spell_sha_stormstrike : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.ImprovedStormstrikeBuff);
        }

        void HandleAfterCast()
        {
            Unit caster = GetCaster().ToUnit();
            AuraEffect talent = caster?.GetAuraEffect(SpellIds.ImprovedStormstrike, 0);
            if (talent != null && RandomHelper.randChance(talent.GetAmount()))
                caster.CastSpell(caster, SpellIds.ImprovedStormstrikeBuff, new CastSpellExtraArgs(talent));
        }

        public override void Register()
        {
            AfterCast.Add(new(HandleAfterCast));
        }
    }

    // 1223031 - Improved Stormstrike (proc part): "Stormstrike's cooldown has a 20% chance to reset each time you Dodge or Parry."
    // (spell_proc: taken melee, dodge or parry; talent effect 2 = chance)
    [Script]
    class classic_spell_sha_improved_stormstrike : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellEffect((SpellIds.ImprovedStormstrike, 2));
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            return GetTarget().GetSpellHistory().HasCooldown(SpellIds.Stormstrike);
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            if (RandomHelper.randChance(aurEff.GetAmount()))
                GetTarget().GetSpellHistory().ResetCooldown(SpellIds.Stormstrike, true);
        }

        void PreventProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(PreventProc, 0, AuraType.Dummy));
            OnEffectProc.Add(new(PreventProc, 1, AuraType.Dummy));
            OnEffectProc.Add(new(HandleProc, 2, AuraType.Dummy));
        }
    }

    // 408341-408345 - Fire Nova: "Instantly inflicts X fire damage to enemies within 10 yd of your active Fire totem." Forever
    // replaced Fire Nova Totem by this spell; its effect is a dummy, the fire totem casts the per rank damage spell (408423-408428,
    // TARGET_DEST_DEST + enemies 10 yd) at its position for the shaman.
    [Script]
    class classic_spell_sha_fire_nova : SpellScript
    {
        static int GetDamageSpell(int spellId)
        {
            switch (spellId)
            {
                case 408341: return 408423;
                case 408342: return 408424;
                case 408343: return 408426;
                case 408344: return 408427;
                case 408345: return 408428;
                default: return 0;
            }
        }

        static Creature GetFireTotem(Unit caster)
        {
            // SummonProperties slot 1 = fire totems (Searing, Magma, Flametongue, Frost Resistance...)
            ObjectGuid guid = caster.m_SummonSlot[(int)SummonSlot.Totem];
            if (guid.IsEmpty())
                return null;

            Creature totem = ObjectAccessor.GetCreature(caster, guid);
            return totem != null && totem.IsTotem() && totem.IsAlive() ? totem : null;
        }

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(GetDamageSpell(spellInfo.Id));
        }

        SpellCastResult CheckCast()
        {
            return GetFireTotem(GetCaster().ToUnit()) != null ? SpellCastResult.SpellCastOk : SpellCastResult.Totems;
        }

        void HandleDummy(int effIndex)
        {
            Unit caster = GetCaster().ToUnit();
            Creature totem = GetFireTotem(caster);
            if (totem == null)
                return;

            totem.CastSpell(totem.GetPosition(), GetDamageSpell(GetSpellInfo().Id), new CastSpellExtraArgs(TriggerCastFlags.FullMask)
                .SetOriginalCaster(caster.GetGUID()));
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 409324 - Ancestral Guidance: "For the next 10 sec, 25% of your damage is converted to healing on up to 3 nearby party members,
    // and 100% of your healing is converted to damage on your most recent Flame Shock target." Damage -> 409333 (party heal, up to
    // 3 targets), healing -> 409337 on the enemy carrying the shaman's Flame Shock with the most time left.
    [Script]
    class classic_spell_sha_ancestral_guidance : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.AncestralGuidanceHeal, SpellIds.AncestralGuidanceDamage)
                && ValidateSpellEffect((SpellIds.AncestralGuidance, 1));
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            if (spellInfo != null && (spellInfo.Id == SpellIds.AncestralGuidanceHeal || spellInfo.Id == SpellIds.AncestralGuidanceDamage))
                return false;

            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            if (damageInfo != null && damageInfo.GetDamage() != 0)
                return true;

            HealInfo healInfo = eventInfo.GetHealInfo();
            return healInfo != null && healInfo.GetEffectiveHeal() != 0;
        }

        static Unit GetFlameShockTarget(Unit shaman)
        {
            List<Unit> enemies = new();
            var check = new AnyUnfriendlyUnitInObjectRangeCheck(shaman, shaman, 40.0f);
            var searcher = new UnitListSearcher(shaman, enemies, check);
            Cell.VisitAllObjects(shaman, searcher, 40.0f);

            Unit best = null;
            int bestDuration = 0;
            foreach (Unit enemy in enemies)
            {
                // Flame Shock (8050, 8052, 8053, 10447, 10448, 29228): shaman family flag 0x10000000
                AuraEffect flameShock = enemy.GetAuraEffect(AuraType.PeriodicDamage, SpellFamilyNames.Shaman, new FlagArray128(0x10000000, 0, 0, 0), shaman.GetGUID());
                if (flameShock != null && flameShock.GetBase().GetDuration() > bestDuration)
                {
                    best = enemy;
                    bestDuration = flameShock.GetBase().GetDuration();
                }
            }
            return best;
        }

        void HandleProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            Unit shaman = GetTarget();

            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            if (damageInfo != null && damageInfo.GetDamage() != 0)
            {
                int heal = MathFunctions.CalculatePct(damageInfo.GetDamage(), aurEff.GetAmount());
                if (heal >= 1)
                    shaman.CastSpell(shaman, SpellIds.AncestralGuidanceHeal, new CastSpellExtraArgs(aurEff)
                        .AddSpellMod(SpellValueMod.BasePoint0, heal).AddSpellMod(SpellValueMod.MaxTargets, 3));
                return;
            }

            HealInfo healInfo = eventInfo.GetHealInfo();
            if (healInfo == null || healInfo.GetEffectiveHeal() == 0)
                return;

            int damage = MathFunctions.CalculatePct(healInfo.GetEffectiveHeal(), GetAura().GetEffect(1).GetAmount());
            if (damage < 1)
                return;

            Unit target = GetFlameShockTarget(shaman);
            if (target != null)
                shaman.CastSpell(target, SpellIds.AncestralGuidanceDamage, new CastSpellExtraArgs(aurEff).AddSpellMod(SpellValueMod.BasePoint0, damage));
        }

        void PreventProc(AuraEffect aurEff, ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnEffectProc.Add(new(HandleProc, 0, AuraType.Dummy));
            OnEffectProc.Add(new(PreventProc, 1, AuraType.Dummy));
        }
    }
}
