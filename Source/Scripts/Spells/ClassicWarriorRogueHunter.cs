// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;
using System.Collections.Generic;
using static Global;

// Classic 1.60 (WoW Forever) warrior, rogue and hunter talents and abilities whose client spell is only a dummy aura or dummy effect, ported
// from the reference TrinityCore (src/server/scripts/Custom/Classic/world/talents/classic_talents_warrior_rogue_hunter.cpp). Talent ranks
// come from TraitDefinitionEffectPoints curves: the core puts the rank value into the aura amount, so the scripts read aurEff.GetAmount()
// and never hardcode rank values.
namespace Scripts.Spells.Classic.WarriorRogueHunter
{
    struct SpellIds
    {
        // Warrior
        public const int TacticalMastery = 1310185;         // baseline (level 14): retain $m1 rage on stance change
        public const int ImprovedTacticalMastery = 12295;
        public const int EnrageBuff = 12880;
        public const int BloodCrazeRegen = 16488;
        public const int MeathookPull = 403233;

        // Rogue
        public const int SetupComboPoint = 15250;
        public const int SealFateComboPoint = 14189;
        public const int CutthroatBuff = 462707;
        public const int Quietus = 1310728;
        public const int Shiv = 424799;
        public const int ShivStrike = 424800;
        public const int PoisonedKnife = 425012;
        public const int PoisonedKnifeStrike = 425013;
        public const int Redirect = 438040;

        // Hunter
        public const int Entrapment = 19184;
        public const int EntrapmentRoot = 19185;
        public const int SpiritBond5Sec = 24529;            // rank 2: 1% every 5 sec (area aura cast by the pet: pet and hunter)
        public const int SpiritBond10Sec = 1310725;         // rank 1: 1% every 10 sec
        public const int FocusedFire = 1223755;
        public const int RapidRecuperation = 1223987;
        public const int RapidRecuperationBuff = 1242512;
        public const int LaceratingStrikesBleed = 1310536;
        public const int KillCommandBuff = 409396;          // 3 stacks of +20% pet Claw / Bite damage (spell mod on the hunter)
        public const int KillCommandPet = 409418;           // on the pet: each Claw / Bite takes one stack
    }

    /*######
    ## Warrior
    ######*/

    // 2457 Battle Stance, 71 Defensive Stance, 2458 Berserker Stance - stance change rage. Changing stances drops the rage above what
    // Tactical Mastery (1310185, baseline at level 14: "You retain up to $m1 Rage") and Improved Tactical Mastery (12295 talent: "retain up
    // to an additional $s1 Rage") keep. Rage is stored x10.
    [Script]
    class classic_spell_warr_stance_rage : AuraScript
    {
        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Unit warrior = GetTarget();
            // not on login (auras load before the player enters the world)
            if (!warrior.IsPlayer() || !warrior.IsInWorld)
                return;

            int retain = 0;
            AuraEffect tacticalMastery = warrior.GetAuraEffect(SpellIds.TacticalMastery, 0);
            if (tacticalMastery != null)
                retain += tacticalMastery.GetAmount();
            AuraEffect improved = warrior.GetAuraEffect(SpellIds.ImprovedTacticalMastery, 0);
            if (improved != null)
                retain += improved.GetAmount();

            retain = Math.Max(0, retain) * 10;
            if (warrior.GetPower(PowerType.Rage) > retain)
                warrior.SetPower(PowerType.Rage, retain);
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 0, AuraType.ModShapeshift, AuraEffectHandleModes.Real));
        }
    }

    // 12317 - Enrage (talent): "Gives you a 30% chance to deal $m1% increased Physical damage for 12 sec after being the victim of any
    // damaging attack." The 30% is the talent proc chance (client data); the buff is the vanilla Enrage 12880 (physical damage done) with the
    // rank amount.
    [Script]
    class classic_spell_warr_enrage_talent : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.EnrageBuff);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            return damageInfo != null && damageInfo.GetDamage() > 0;
        }

        void HandleProc(ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            AuraEffect talent = GetEffect(0);
            if (talent == null || talent.GetAmount() <= 0)
                return;

            Unit warrior = GetTarget();
            warrior.CastSpell(warrior, SpellIds.EnrageBuff, new CastSpellExtraArgs(talent)
                .SetTriggeringSpell(eventInfo.GetProcSpell())
                .AddSpellMod(SpellValueMod.BasePoint0, talent.GetAmount()));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnProc.Add(new(HandleProc));
        }
    }

    // 16487 - Blood Craze: "Regenerates $m1% of your total Health over 6 sec after being the victim of a critical strike or suffering more
    // than $m2% of your maximum Health from a single attack." 16488 heals a flat amount every 2 sec, so the rank percent of max health is
    // split over its ticks. (The default dummy proc cast 16488 with its 1 point base on every hit.)
    [Script]
    class classic_spell_warr_blood_craze : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.BloodCrazeRegen) && ValidateSpellEffect((spellInfo.Id, 1));
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            if (damageInfo == null || damageInfo.GetDamage() == 0)
                return false;

            if (eventInfo.GetHitMask().HasAnyFlag(ProcFlagsHit.Critical))
                return true;

            AuraEffect threshold = GetEffect(1);
            return threshold != null && damageInfo.GetDamage() > GetTarget().CountPctFromMaxHealth(threshold.GetAmount());
        }

        void HandleProc(ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            AuraEffect talent = GetEffect(0);
            SpellInfo regen = SpellMgr.GetSpellInfo(SpellIds.BloodCrazeRegen, Difficulty.None);
            if (talent == null || talent.GetAmount() <= 0 || regen == null || regen.GetEffect(0).ApplyAuraPeriod == 0)
                return;

            Unit warrior = GetTarget();
            int ticks = Math.Max(1, regen.GetMaxDuration() / (int)regen.GetEffect(0).ApplyAuraPeriod);
            int perTick = Math.Max(1, (int)(warrior.CountPctFromMaxHealth(talent.GetAmount()) / ticks));
            warrior.CastSpell(warrior, SpellIds.BloodCrazeRegen, new CastSpellExtraArgs(talent).AddSpellMod(SpellValueMod.BasePoint0, perTick));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnProc.Add(new(HandleProc));
        }
    }

    // 1290261 - Weaponmaster (warrior) and 13960 - Hack and Slash (rogue), Forever talents: "Gives your melee weapon attacks a benefit
    // depending on the weapon." The client data has a hidden passive per weapon group with the weapon requirement (SpellEquippedItems):
    //   Weaponmaster:   $s1 crit    Axe/Polearm -> 12700 (aura 290)     $s2 armor ignored  Mace/Staff -> 12284 (aura 280)
    //                   $s3 extra attack chance  Sword -> 12281 (proc 1257049 extra attack)
    //   Hack and Slash: $s1 crit    Dagger/Fist -> 13706 (aura 290)     $s2 armor ignored  Mace -> 13709 (aura 280)
    //                   $s3 extra attack chance  Axe/Sword -> 1290312 (proc 16459 extra attack)
    // The talent keeps the passive that fits the equipped weapon on the player with the rank value (effect i of the talent -> passive i).
    // The core removes item dependent auras when the weapon is unequipped; the heartbeat adds the passive back for a fitting weapon.
    [Script("classic_spell_warr_weaponmaster", 12700, 12284, 12281)]
    [Script("classic_spell_rog_hack_and_slash", 13706, 13709, 1290312)]
    class classic_spell_weapon_type_talent : AuraScript
    {
        readonly int[] _passives;

        public classic_spell_weapon_type_talent(int critSpell, int armorSpell, int extraAttackSpell)
        {
            _passives = new[] { critSpell, armorSpell, extraAttackSpell };
        }

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(_passives) && ValidateSpellEffect((spellInfo.Id, 2));
        }

        void UpdatePassives()
        {
            Player player = GetUnitOwner().ToPlayer();
            if (player == null)
                return;

            for (int i = 0; i < _passives.Length; ++i)
            {
                int passiveId = _passives[i];
                SpellInfo passive = SpellMgr.GetSpellInfo(passiveId, Difficulty.None);
                AuraEffect talent = GetEffect((byte)i);
                int amount = talent != null ? talent.GetAmount() : 0;

                Aura current = player.GetAura(passiveId, player.GetGUID());
                if (passive == null || amount <= 0 || !player.HasItemFitToSpellRequirements(passive))
                {
                    if (current != null)
                        player.RemoveAurasDueToSpell(passiveId, player.GetGUID());
                    continue;
                }

                if (current != null)
                {
                    AuraEffect currentEffect = current.GetEffect(0);
                    if (currentEffect != null && currentEffect.GetAmount() == amount)
                        continue;
                    player.RemoveAurasDueToSpell(passiveId, player.GetGUID());
                }

                player.CastSpell(player, passiveId, new CastSpellExtraArgs(TriggerCastFlags.FullMask).AddSpellMod(SpellValueMod.BasePoint0, amount));
            }
        }

        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            UpdatePassives();
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Unit owner = GetUnitOwner();
            foreach (int passiveId in _passives)
                owner.RemoveAurasDueToSpell(passiveId, owner.GetGUID());
        }

        // Hack and Slash has a 1% dummy proc in the client data; the passives do the work
        bool CheckProc(ProcEventInfo eventInfo)
        {
            return false;
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
            AfterEffectRemove.Add(new(HandleRemove, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
            OnHeartbeat.Add(new(UpdatePassives));
            DoCheckProc.Add(new(CheckProc));
        }
    }

    // 12281 - Weaponmaster (sword), 1290312 - Hack and Slash (axe/sword): extra attack passives of the talents above. The proc chance is the
    // talent rank ($s3, put into this aura amount by classic_spell_weapon_type_talent; spell_proc Chance 100).
    [Script]
    class classic_spell_weapon_extra_attack_chance : AuraScript
    {
        bool CheckProc(ProcEventInfo eventInfo)
        {
            AuraEffect chance = GetEffect(0);
            return chance != null && RandomHelper.randChance(chance.GetAmount());
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
        }
    }

    // 403228 - Meathook (warrior ability): "Throw out a hooked chain to restrain the target and pull them toward you. May not be used on
    // enemies higher than level 61 or which are marked as boss enemies." The client spell is a dummy; the target jumps in front of the
    // warrior with 403233 (jump to destination, stun immunity while pulled).
    [Script]
    class classic_spell_warr_meathook : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.MeathookPull);
        }

        SpellCastResult CheckCast()
        {
            Unit target = GetExplTargetUnit();
            if (target == null)
                return SpellCastResult.BadTargets;

            if (target.GetLevel() > 61)
                return SpellCastResult.Highlevel;

            Creature creature = target.ToCreature();
            if (creature != null && (creature.IsWorldBoss() || creature.IsDungeonBoss()))
                return SpellCastResult.BadTargets;

            return SpellCastResult.SpellCastOk;
        }

        void HandleDummy(int effIndex)
        {
            Unit warrior = GetCaster().ToUnit();
            Unit target = GetHitUnit();
            if (target == null || target == warrior || !target.IsAlive())
                return;

            Position dest = warrior.GetFirstCollisionPosition(warrior.GetCombatReach() + target.GetCombatReach(), 0.0f);
            target.CastSpell(dest, SpellIds.MeathookPull, new CastSpellExtraArgs(TriggerCastFlags.FullMask)
                .SetOriginalCaster(warrior.GetGUID())
                .SetTriggeringSpell(GetSpell()));
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    /*######
    ## Rogue
    ######*/

    // 13983 - Setup: "Gives you a $m1% chance to add a Combo Point to your target after Dodging one of their attacks or fully resisting one
    // of their spells. (1s cooldown)" spell_proc: dodge / full resist only, 1 s cooldown. 15250 gives the point; cast on the attacker, so
    // Spell::EffectEnergize makes it the combo target. Like VMaNGOS, only when the attacker is the rogue's current victim.
    [Script]
    class classic_spell_rog_setup : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.SetupComboPoint);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            Unit attacker = eventInfo.GetActor();
            AuraEffect talent = GetEffect(0);
            if (attacker == null || attacker == GetTarget() || !attacker.IsAlive() || GetTarget().GetVictim() != attacker || talent == null)
                return false;

            return RandomHelper.randChance(talent.GetAmount());
        }

        void HandleProc(ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            GetTarget().CastSpell(eventInfo.GetActor(), SpellIds.SetupComboPoint, new CastSpellExtraArgs(GetEffect(0)));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnProc.Add(new(HandleProc));
        }
    }

    // 14186 - Seal Fate: "Your critical strikes from abilities that add Combo Points have a $m1% chance to add an additional Combo Point.
    // (500ms cooldown)" spell_proc: crits only, 500 ms cooldown, triggered spells may proc (Shiv strike). 14189 adds the point on the target.
    [Script]
    class classic_spell_rog_seal_fate : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.SealFateComboPoint);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            AuraEffect talent = GetEffect(0);
            Unit target = eventInfo.GetActionTarget();
            if (talent == null || target == null || target == GetTarget() || !target.IsAlive() || !ClassicSpells.IsComboPointBuilder(eventInfo.GetSpellInfo()))
                return false;

            return RandomHelper.randChance(talent.GetAmount());
        }

        void HandleProc(ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            GetTarget().CastSpell(eventInfo.GetActionTarget(), SpellIds.SealFateComboPoint, new CastSpellExtraArgs(GetEffect(0)));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnProc.Add(new(HandleProc));
        }
    }

    // 462708 - Cutthroat: "Your Backstab has a $m1% chance to cause your next Ambush within 10 sec to not require Stealth." 462707 (aura 275,
    // ignore the stance requirement of Ambush, 1 charge) is the client buff; its charge is used by the next Ambush (spell_proc).
    [Script]
    class classic_spell_rog_cutthroat : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.CutthroatBuff);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            AuraEffect talent = GetEffect(0);
            // Backstab (rogue family flag 0x4)
            if (talent == null || spellInfo == null || spellInfo.SpellFamilyName != SpellFamilyNames.Rogue || (spellInfo.SpellFamilyFlags[0] & 0x00000004) == 0)
                return false;

            return RandomHelper.randChance(talent.GetAmount());
        }

        void HandleProc(ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            GetTarget().CastSpell(GetTarget(), SpellIds.CutthroatBuff, new CastSpellExtraArgs(GetEffect(0)));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnProc.Add(new(HandleProc));
        }
    }

    // 424799 - Shiv, 425012 - Poisoned Knife (rogue abilities): the client spells are dummies that cast the off-hand strike (424800 / 425013,
    // off-hand normalized weapon damage, awards the combo point).
    [Script]
    class classic_spell_rog_offhand_strike : SpellScript
    {
        static int GetStrikeSpell(int spellId)
        {
            return spellId == SpellIds.PoisonedKnife ? SpellIds.PoisonedKnifeStrike : SpellIds.ShivStrike;
        }

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(GetStrikeSpell(spellInfo.Id));
        }

        SpellCastResult CheckCast()
        {
            Player player = GetCaster().ToPlayer();
            if (player != null && player.GetWeaponForAttack(WeaponAttackType.OffAttack, true) == null)
                return SpellCastResult.EquippedItemClass;

            return SpellCastResult.SpellCastOk;
        }

        void HandleDummy(int effIndex)
        {
            Unit target = GetHitUnit();
            if (target != null)
                GetCaster().CastSpell(target, GetStrikeSpell(GetSpellInfo().Id), new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetTriggeringSpell(GetSpell()));
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 424800 - Shiv, 425013 - Poisoned Knife (the strikes): "with a 100% chance to apply the poison from your off-hand weapon to the target".
    // Poisoned Knife also "Refunds $s3 Energy per application of Deadly Poison on the target" (425012 effect 2; counted before the new poison).
    [Script]
    class classic_spell_rog_offhand_poison : SpellScript
    {
        const uint DeadlyPoisonFamilyFlag = 0x00010000;

        void HandleHit(int effIndex)
        {
            Player rogue = GetCaster().ToPlayer();
            Unit target = GetHitUnit();
            if (rogue == null || target == null || !target.IsAlive())
                return;

            if (GetSpellInfo().Id == SpellIds.PoisonedKnifeStrike)
            {
                int applications = 0;
                foreach (AuraEffect periodic in target.GetAuraEffectsByType(AuraType.PeriodicDamage))
                {
                    SpellInfo poison = periodic.GetSpellInfo();
                    if (periodic.GetCasterGUID() == rogue.GetGUID() && poison.SpellFamilyName == SpellFamilyNames.Rogue
                        && (poison.SpellFamilyFlags[0] & DeadlyPoisonFamilyFlag) != 0)
                        applications += periodic.GetBase().GetStackAmount();
                }

                SpellInfo knife = SpellMgr.GetSpellInfo(SpellIds.PoisonedKnife, Difficulty.None);
                if (applications != 0 && knife != null && knife.GetEffects().Count > 2)
                {
                    int refund = knife.GetEffect(2).CalcValue(rogue) * applications;
                    if (refund > 0)
                        rogue.EnergizeBySpell(rogue, knife, refund, PowerType.Energy);
                }
            }

            Item offHand = rogue.GetWeaponForAttack(WeaponAttackType.OffAttack, true);
            if (offHand == null)
                return;

            SpellItemEnchantmentRecord enchant = CliDB.SpellItemEnchantmentStorage.LookupByKey(offHand.GetEnchantmentId(EnchantmentSlot.EnhancementTemporary));
            if (enchant == null)
                return;

            for (int s = 0; s < ItemConst.MaxItemEnchantmentEffects; ++s)
            {
                if (enchant.Effect(s) != ItemEnchantmentType.CombatSpell)
                    continue;

                SpellInfo poison = SpellMgr.GetSpellInfo(enchant.EffectArg[s], Difficulty.None);
                if (poison == null)
                    continue;

                rogue.CastSpell(poison.IsPositive() ? rogue : target, poison.Id, new CastSpellExtraArgs(offHand));
            }
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleHit, 0, SpellEffectName.NormalizedWeaponDmg));
        }
    }

    // 438040 - Redirect (rogue ability): "Removes all your existing combo points from your target and stores them for up to 15 sec. These
    // stored combo points will be transferred to the next non-player enemy you hit with a melee or ranged ability." The stored points are
    // kept in the self aura (effect 1, 1 proc charge); the transfer sets UnitData ClassicComboTarget like Spell::EffectEnergize.
    [Script]
    class classic_spell_rog_redirect_SpellScript : SpellScript
    {
        SpellCastResult CheckCast()
        {
            return GetCaster().ToUnit().GetPower(PowerType.ComboPoints) > 0 ? SpellCastResult.SpellCastOk : SpellCastResult.NoComboPoints;
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
        }
    }

    [Script]
    class classic_spell_rog_redirect_AuraScript : AuraScript
    {
        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Unit rogue = GetTarget();
            GetEffect(1)?.SetAmount(rogue.GetPower(PowerType.ComboPoints));
            rogue.SetPower(PowerType.ComboPoints, 0);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            Unit target = eventInfo.GetActionTarget();
            AuraEffect stored = GetEffect(1);
            if (stored == null || stored.GetAmount() <= 0 || target == null || target == GetTarget() || !target.IsAlive())
                return false;

            if (target.IsCharmedOwnedByPlayerOrPlayer() || !GetTarget().IsValidAttackTarget(target))
                return false;

            return eventInfo.GetSpellInfo() == null || eventInfo.GetSpellInfo().Id != SpellIds.Redirect;
        }

        void HandleProc(ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            Unit rogue = GetTarget();
            Unit target = eventInfo.GetActionTarget();
            int points = GetEffect(1).GetAmount();
            // the ability may have already put points on this target
            if (rogue.GetClassicComboTarget() == target.GetGUID())
                points += rogue.GetPower(PowerType.ComboPoints);

            rogue.SetClassicComboTarget(target.GetGUID());
            rogue.SetPower(PowerType.ComboPoints, Math.Min(points, rogue.GetMaxPower(PowerType.ComboPoints)));
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 1, AuraType.Dummy, AuraEffectHandleModes.Real));
            DoCheckProc.Add(new(CheckProc));
            OnProc.Add(new(HandleProc));
        }
    }

    /*######
    ## Hunter
    ######*/

    // 19184 - Entrapment: "When your traps are triggered, all affected targets are Entrapped, preventing them from moving for ${$m1/1000}
    // sec." The rank amount is the root duration in ms (1000-5000) on the vanilla root 19185. Trap effects are cast by the trap object with
    // the hunter as original caster: Freezing Trap 3355/14308/14309, Immolation Trap 13797/14298-14301, Explosive Trap 13812/14314/14315.
    static class ClassicEntrapment
    {
        public static void Entrap(Unit hunter, Unit target)
        {
            if (hunter == null || target == null || hunter == target || !target.IsAlive() || !hunter.IsValidAttackTarget(target))
                return;

            AuraEffect entrapment = hunter.GetAuraEffect(SpellIds.Entrapment, 0);
            if (entrapment == null || entrapment.GetAmount() <= 0)
                return;

            hunter.CastSpell(target, SpellIds.EntrapmentRoot, new CastSpellExtraArgs(TriggerCastFlags.FullMask)
                .AddSpellMod(SpellValueMod.Duration, entrapment.GetAmount()));
        }
    }

    [Script]
    class classic_spell_hun_entrapment_trap : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.EntrapmentRoot);
        }

        void HandleHit()
        {
            ClassicEntrapment.Entrap(GetOriginalCaster(), GetHitUnit());
        }

        public override void Register()
        {
            AfterHit.Add(new(HandleHit));
        }
    }

    // 13810 - Frost Trap Aura: the trap makes a persistent area; Entrapment roots each target when the slow lands on it
    [Script]
    class classic_spell_hun_entrapment_frost_trap : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.EntrapmentRoot);
        }

        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            ClassicEntrapment.Entrap(GetCaster(), GetTarget());
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 0, AuraType.ModDecreaseSpeed, AuraEffectHandleModes.Real));
        }
    }

    // 19578 - Spirit Bond: "While your pet is active, you and your pet will regenerate $m1% of total health every $m2 sec." $m2 is 10 at
    // rank 1 and 5 at rank 2 (trait curve on the dummy effect 1). The client regen spells are area pet auras (cast by the pet, on the pet
    // and its owner): 1310725 every 10 sec, 24529 every 5 sec. The pet gets the right one when the talent applies and on every heartbeat.
    [Script]
    class classic_spell_hun_spirit_bond : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.SpiritBond5Sec, SpellIds.SpiritBond10Sec) && ValidateSpellEffect((spellInfo.Id, 1));
        }

        int GetRegenSpell()
        {
            int period = GetSpellInfo().GetEffect(1).CalcValue(GetUnitOwner());
            return period > 5 ? SpellIds.SpiritBond10Sec : SpellIds.SpiritBond5Sec;
        }

        void UpdatePet()
        {
            Player hunter = GetUnitOwner().ToPlayer();
            Pet pet = hunter?.GetPet();
            if (pet == null || !pet.IsAlive() || !pet.IsInWorld)
                return;

            int regen = GetRegenSpell();
            int other = regen == SpellIds.SpiritBond5Sec ? SpellIds.SpiritBond10Sec : SpellIds.SpiritBond5Sec;
            pet.RemoveAurasDueToSpell(other, pet.GetGUID());
            if (!pet.HasAura(regen, pet.GetGUID()))
                pet.CastSpell(pet, regen, new CastSpellExtraArgs(TriggerCastFlags.FullMask));
        }

        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            UpdatePet();
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Player hunter = GetUnitOwner().ToPlayer();
            if (hunter == null)
                return;

            foreach (int regen in new[] { SpellIds.SpiritBond5Sec, SpellIds.SpiritBond10Sec })
            {
                hunter.RemoveAurasDueToSpell(regen);
                hunter.GetPet()?.RemoveAurasDueToSpell(regen);
            }
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
            AfterEffectRemove.Add(new(HandleRemove, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
            OnHeartbeat.Add(new(UpdatePet));
        }
    }

    // 1223987 - Rapid Recuperation: "Hitting a target with your Serpent Sting ability grants you $s1% and consuming Rapid Killing grants you
    // $s2% of your Mana regeneration while casting for the next 15 sec." Buff 1242512 (aura 134) with the rank amount. Serpent Sting: the
    // talent proc (ranged ability hit, hunter family flag 0x4000). Rapid Killing: classic_spell_hun_rapid_killing_consumed.
    [Script]
    class classic_spell_hun_rapid_recuperation : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.RapidRecuperationBuff);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            return spellInfo != null && spellInfo.SpellFamilyName == SpellFamilyNames.Hunter && (spellInfo.SpellFamilyFlags[0] & 0x00004000) != 0
                && spellInfo.HasAura(AuraType.PeriodicDamage);
        }

        void HandleProc(ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            AuraEffect talent = GetEffect(0);
            if (talent == null || talent.GetAmount() <= 0)
                return;

            GetTarget().CastSpell(GetTarget(), SpellIds.RapidRecuperationBuff, new CastSpellExtraArgs(talent).AddSpellMod(SpellValueMod.BasePoint0, talent.GetAmount()));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnProc.Add(new(HandleProc));
        }
    }

    // 415407 - Rapid Killing (buff, 1 charge used by the next shot): when the charge is consumed, Rapid Recuperation gives $s2% (effect 1)
    [Script]
    class classic_spell_hun_rapid_killing_consumed : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.RapidRecuperationBuff, SpellIds.RapidRecuperation);
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            // consumed charges remove the aura with the default mode; expiry, death and cancel use their own
            if (GetTargetApplication().GetRemoveMode() != AuraRemoveMode.Default)
                return;

            Unit hunter = GetTarget();
            AuraEffect talent = hunter.GetAuraEffect(SpellIds.RapidRecuperation, 1);
            if (talent == null || talent.GetAmount() <= 0)
                return;

            hunter.CastSpell(hunter, SpellIds.RapidRecuperationBuff, new CastSpellExtraArgs(talent).AddSpellMod(SpellValueMod.BasePoint0, talent.GetAmount()));
        }

        public override void Register()
        {
            AfterEffectRemove.Add(new(HandleRemove, 0, AuraType.AddPctModifier, AuraEffectHandleModes.Real));
        }
    }

    // 1310533 - Lacerating Strikes: "Your Mongoose Bite also causes the target to Bleed for damage equal to $s1% of the damage done by
    // Mongoose Bite over 21 sec" - bleed 1310536 (periodic damage every 3 sec), the total split over its ticks.
    [Script]
    class classic_spell_hun_lacerating_strikes : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.LaceratingStrikesBleed);
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            DamageInfo damageInfo = eventInfo.GetDamageInfo();
            // Mongoose Bite: hunter family flags 0x2 (word 0) and 0x1 (word 3)
            return spellInfo != null && damageInfo != null && damageInfo.GetDamage() > 0 && eventInfo.GetActionTarget() != null
                && spellInfo.SpellFamilyName == SpellFamilyNames.Hunter && (spellInfo.SpellFamilyFlags[0] & 0x00000002) != 0 && (spellInfo.SpellFamilyFlags[3] & 0x00000001) != 0;
        }

        void HandleProc(ProcEventInfo eventInfo)
        {
            PreventDefaultAction();

            AuraEffect talent = GetEffect(0);
            SpellInfo bleed = SpellMgr.GetSpellInfo(SpellIds.LaceratingStrikesBleed, Difficulty.None);
            if (talent == null || talent.GetAmount() <= 0 || bleed == null || bleed.GetEffect(0).ApplyAuraPeriod == 0)
                return;

            int ticks = Math.Max(1, bleed.GetMaxDuration() / (int)bleed.GetEffect(0).ApplyAuraPeriod);
            int total = MathFunctions.CalculatePct(eventInfo.GetDamageInfo().GetDamage(), talent.GetAmount());
            GetTarget().CastSpell(eventInfo.GetActionTarget(), SpellIds.LaceratingStrikesBleed, new CastSpellExtraArgs(talent)
                .AddSpellMod(SpellValueMod.BasePoint0, Math.Max(1, total / ticks)));
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            OnProc.Add(new(HandleProc));
        }
    }

    // 409379 - Kill Command (hunter ability): "Give the command to kill, increasing your pet's damage done from Claw and Bite by 60% for 30
    // sec. Each Claw or Bite done by the pet reduces the damage bonus by 20%." The client spell is a dummy aura; 409396 is the client buff
    // (+20% Claw / Bite per stack, a spell mod on the hunter that pet spells use) and triggers 409418 on the pet, whose proc takes one stack.
    [Script]
    class classic_spell_hun_kill_command_SpellScript : SpellScript
    {
        SpellCastResult CheckCast()
        {
            Player hunter = GetCaster().ToPlayer();
            Pet pet = hunter?.GetPet();
            if (pet == null || !pet.IsAlive())
                return SpellCastResult.NoPet;

            return SpellCastResult.SpellCastOk;
        }

        public override void Register()
        {
            OnCheckCast.Add(new(CheckCast));
        }
    }

    [Script]
    class classic_spell_hun_kill_command_AuraScript : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(SpellIds.KillCommandBuff, SpellIds.KillCommandPet);
        }

        void HandleApply(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            Unit hunter = GetTarget();
            hunter.CastSpell(hunter, SpellIds.KillCommandBuff, new CastSpellExtraArgs(aurEff));
            Aura buff = hunter.GetAura(SpellIds.KillCommandBuff, hunter.GetGUID());
            buff?.SetStackAmount((byte)(buff.GetSpellInfo().StackAmount != 0 ? buff.GetSpellInfo().StackAmount : 3));
        }

        void HandleRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            GetTarget().RemoveAurasDueToSpell(SpellIds.KillCommandBuff, GetTarget().GetGUID());
        }

        public override void Register()
        {
            AfterEffectApply.Add(new(HandleApply, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
            AfterEffectRemove.Add(new(HandleRemove, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
        }
    }

    // 409418 - Kill Command (on the pet): each pet Claw / Bite (hunter family flag 0x8, word 3) takes one stack of the hunter's 409396; the
    // pet aura (1 charge) stays until the last stack is used.
    [Script]
    class classic_spell_hun_kill_command_pet : AuraScript
    {
        Aura GetHunterBuff()
        {
            Unit hunter = GetCaster();
            return hunter?.GetAura(SpellIds.KillCommandBuff, hunter.GetGUID());
        }

        bool CheckProc(ProcEventInfo eventInfo)
        {
            SpellInfo spellInfo = eventInfo.GetSpellInfo();
            return spellInfo != null && spellInfo.SpellFamilyName == SpellFamilyNames.Hunter && (spellInfo.SpellFamilyFlags[3] & 0x00000008) != 0;
        }

        void PrepareProc(ProcEventInfo eventInfo)
        {
            // keep the charge while the hunter has more than one stack left
            Aura buff = GetHunterBuff();
            if (buff != null && buff.GetStackAmount() > 1)
                PreventDefaultAction();
        }

        void HandleProc(ProcEventInfo eventInfo)
        {
            PreventDefaultAction();
            GetHunterBuff()?.ModStackAmount(-1);
        }

        public override void Register()
        {
            DoCheckProc.Add(new(CheckProc));
            DoPrepareProc.Add(new(PrepareProc));
            OnProc.Add(new(HandleProc));
        }
    }

    /*######
    ## Damage done talents without a client aura effect
    ######*/

    // 1223755 - Focused Fire (hunter): "Increases all damage you and your pet deal by $s1% while your pet is active."
    // 1310728 - Quietus (rogue): "Your Sinister Strike, Ghostly Strike, and Hemorrhage abilities cause $m1% more damage against targets
    // below $m2% health." (rogue family flags 0x2 Sinister Strike, 0x4000000 Ghostly Strike, 0x2000000 Hemorrhage; Shiv and Poisoned Knife
    // "benefit from all talents that modify Sinister Strike")
    [Script]
    class classic_unit_talent_damage_mods : UnitScript
    {
        public classic_unit_talent_damage_mods() : base("classic_unit_talent_damage_mods") { }

        static float GetFocusedFirePct(Unit attacker)
        {
            if (attacker == null)
                return 0.0f;

            Player hunter = attacker.ToPlayer();
            if (hunter != null)
            {
                AuraEffect talent = hunter.GetAuraEffect(SpellIds.FocusedFire, 0);
                if (talent == null)
                    return 0.0f;

                Pet pet = hunter.GetPet();
                return pet != null && pet.IsAlive() ? talent.GetAmount() : 0.0f;
            }

            if (attacker.IsPet())
            {
                hunter = attacker.GetCharmerOrOwnerPlayerOrPlayerItself();
                if (hunter == null || hunter == attacker || hunter.GetPet() != attacker)
                    return 0.0f;

                AuraEffect talent = hunter.GetAuraEffect(SpellIds.FocusedFire, 0);
                if (talent != null)
                    return talent.GetAmount();
            }

            return 0.0f;
        }

        static float GetQuietusPct(Unit target, Unit attacker, SpellInfo spellInfo)
        {
            if (target == null || attacker == null || spellInfo == null || !attacker.IsPlayer() || spellInfo.SpellFamilyName != SpellFamilyNames.Rogue)
                return 0.0f;

            if ((spellInfo.SpellFamilyFlags[0] & 0x06000002) == 0 && spellInfo.Id != SpellIds.ShivStrike && spellInfo.Id != SpellIds.PoisonedKnifeStrike)
                return 0.0f;

            AuraEffect bonus = attacker.GetAuraEffect(SpellIds.Quietus, 0);
            AuraEffect threshold = attacker.GetAuraEffect(SpellIds.Quietus, 1);
            if (bonus == null || threshold == null || !target.HealthBelowPct(threshold.GetAmount()))
                return 0.0f;

            return bonus.GetAmount();
        }

        public override void ModifyMeleeDamage(Unit target, Unit attacker, ref int damage)
        {
            float pct = GetFocusedFirePct(attacker);
            if (pct > 0.0f)
                MathFunctions.AddPct(ref damage, pct);
        }

        public override void ModifySpellDamageTaken(Unit target, Unit attacker, ref int damage, SpellInfo spellInfo)
        {
            if (damage <= 0)
                return;

            float pct = GetFocusedFirePct(attacker) + GetQuietusPct(target, attacker, spellInfo);
            if (pct > 0.0f)
                MathFunctions.AddPct(ref damage, pct);
        }

        public override void ModifyPeriodicDamageAurasTick(Unit target, Unit attacker, ref int damage)
        {
            float pct = GetFocusedFirePct(attacker);
            if (pct > 0.0f)
                MathFunctions.AddPct(ref damage, pct);
        }
    }
}
