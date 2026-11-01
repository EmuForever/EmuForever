// Copyright (c) EmuForever <http://github.com/EmuForever> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.AI;
using Game.Entities;
using Game.Scripting;
using Game.Spells;

// Classic 1.60 (WoW Forever) camping scripts, ported from the reference EmuForever (scripts/Custom/Classic/world/classic_go_scripts.cpp).
// The world database binds these names (spell_script_names, gameobject_template.ScriptName). The rules live in Player.Camp.cs,
// which the stand-state change also uses.
namespace Scripts.World.ClassicCamp
{
    [Script] // 1289723 - Campfire Rest
    class classic_spell_campfire_rest : AuraScript
    {
        void AfterRemove(AuraEffect aurEff, AuraEffectHandleModes mode)
        {
            // Only a full minute pays out; standing up (or any other removal) ends the rest without benefits.
            if (GetTargetApplication().GetRemoveMode() != AuraRemoveMode.Expire)
                return;

            GetTarget().ToPlayer()?.CompleteCampfireRest();
        }

        public override void Register()
        {
            AfterEffectRemove.Add(new(AfterRemove, 0, AuraType.Any, AuraEffectHandleModes.Real));
        }
    }

    [Script] // 1229451 - Boosted Rest (Camp Tent)
    class classic_spell_boosted_rest : SpellScript
    {
        void HandleDummy(int effIndex)
        {
            GetHitUnit()?.ToPlayer()?.ApplyBoostedRest(GetEffectValue());
        }

        public override void Register()
        {
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    // 612275 - Camp Chair. Sitting in it goes through GameObject Use; the stand-state change it causes starts the rest
    // (Player.UpdateCampfireRest). This also covers a chair whose seat does not change the stand state.
    [Script]
    class classic_go_camp_chair : GameObjectAI
    {
        public classic_go_camp_chair(GameObject go) : base(go) { }

        public override bool OnGossipHello(Player player)
        {
            if (!player.HasAura(Player.SpellCampfireRest) && player.FindCampfire() != null)
                player.CastSpell(player, Player.SpellCampfireRest, true);
            return false; // let the chair seat the player as usual
        }
    }
}
