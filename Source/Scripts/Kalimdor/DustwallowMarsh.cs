/*
 * This file is part of the TrinityCore Project. See AUTHORS file for Copyright information
 *
 * This program is free software; you can redistribute it and/or modify it
 * under the terms of the GNU General Public License as published by the
 * Free Software Foundation; either version 2 of the License, or (at your
 * option) any later version.
 *
 * This program is distributed in the hope that it will be useful, but WITHOUT
 * ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
 * FITNESS FOR A PARTICULAR PURPOSE. See the GNU General Public License for
 * more details.
 *
 * You should have received a copy of the GNU General Public License along
 * with this program. If not, see <http://www.gnu.org/licenses/>.
 */

// C# port of TrinityCore wotlk_classic.
using Framework.Constants;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;
using System.Collections.Generic;

namespace Scripts.Kalimdor.DustwallowMarsh
{
    [Script]
    class spell_ooze_zap : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo) => ValidateSpellEffect((spellInfo.Id, 1)) && ValidateSpellInfo(42489);
        SpellCastResult CheckRequirement()
        {
            if (!GetCaster().HasAura(GetEffectInfo(1).CalcValue()))
                return SpellCastResult.CantDoThatRightNow;
            return GetExplTargetUnit() == null ? SpellCastResult.BadTargets : SpellCastResult.SpellCastOk;
        }

        void HandleDummy(int effIndex)
        {
            PreventHitDefaultEffect(effIndex);
            if (GetHitUnit() != null)
                GetCaster().CastSpell(GetHitUnit(), GetEffectValue(), true);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
            OnCheckCast.Add(new(CheckRequirement));
        }
    }

    [Script]
    class spell_ooze_zap_channel_end : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo) => ValidateSpellInfo(42486);

        void HandleDummy(int effIndex)
        {
            PreventHitDefaultEffect(effIndex);
            Player player = GetCaster().ToPlayer();
            if (player != null)
                player.CastSpell(player, 42486, true);
            GetHitUnit().KillSelf();
        }

        public override void Register() => OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
    }

    [Script]
    class spell_energize_aoe : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo) => ValidateSpellInfo(42492);
        void FilterTargets(List<WorldObject> targets)
        {
            targets.RemoveAll(target => target.ToPlayer() == null ||
                target.ToPlayer().GetQuestStatus(GetEffectInfo(1).CalcValue()) != QuestStatus.Incomplete);
            targets.Add(GetCaster());
        }

        void HandleScript(int effIndex)
        {
            PreventHitDefaultEffect(effIndex);
            GetCaster().CastSpell(GetCaster(), GetEffectValue(), true);
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleScript, 0, SpellEffectName.ScriptEffect));
            OnObjectAreaTargetSelect.Add(new(FilterTargets, 0, Targets.UnitSrcAreaEntry));
            OnObjectAreaTargetSelect.Add(new(FilterTargets, 1, Targets.UnitSrcAreaEntry));
        }
    }

    [Script]
    class spell_dustwallow_marsh_salvage_wreckage : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo) => ValidateSpellInfo(42288, 42289);
        void HandleDummy(int effIndex) => GetCaster().CastSpell(GetCaster(), Random.Shared.Next(2) == 0 ? 42288 : 42289, false);
        public override void Register() => OnEffectHit.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
    }

}

