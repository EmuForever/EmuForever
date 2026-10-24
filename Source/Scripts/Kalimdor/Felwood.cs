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
using Game.AI;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;

namespace Scripts.Kalimdor.Felwood
{
    [Script]
    class npc_whisperwind_lasher : ScriptedAI
    {
        bool _clicked;
        public npc_whisperwind_lasher(Creature creature) : base(creature) { }
        public override void Reset() => _clicked = false;
        public override void OnSpellClick(Unit clicker, ref bool spellClickHandled)
        {
            if (!spellClickHandled) return;
            if (Random.Shared.Next(100) < 30)
            {
                me.CastSpell(me, 52225, false);
                me.SetEntry(48387);
                me.SetFaction(14);
            }
            else me.SetUninteractible(true);
            me.RemoveNpcFlag(NPCFlags1.SpellClick);
            me.CastSpell(me, 37752, false);
            me.GetMotionMaster().MoveRandom(8);
            _events.ScheduleEvent(1, TimeSpan.FromSeconds(20));
            _clicked = true;
            clicker.ToPlayer()?.KilledMonsterCredit(47747);
        }
        public override void UpdateAI(TimeSpan diff)
        {
            if (!_clicked) return;
            _events.Update(diff);
            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
                if (eventId == 1)
                {
                    if (!me.IsInCombat()) me.DespawnOrUnsummon();
                    else _events.ScheduleEvent(1, TimeSpan.FromSeconds(5));
                }
        }
    }

    [Script]
    class spell_swipe_honey : SpellScript
    {
        SpellCastResult CheckTarget() => GetCaster().FindNearestCreature(47308, 5, true) != null
            ? SpellCastResult.SpellCastOk : SpellCastResult.OutOfRange;
        void HandleDummy(int effIndex)
        {
            Creature honey = GetCaster().FindNearestCreature(47308, 5, true);
            Player player = GetCaster().ToPlayer();
            if (honey == null || player == null) return;
            player.AddItem(62820, 1);
            if (Random.Shared.Next(1, 101) < 30) honey.CastSpell(player, 94064, false);
            else honey.DespawnOrUnsummon();
        }
        public override void Register()
        {
            OnCheckCast.Add(new(CheckTarget));
            OnEffectHitTarget.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
        }
    }

    [Script]
    class spell_beesbees : SpellScript
    {
        void HandleScriptEffect(int effIndex)
        {
            Creature honey = GetCaster().ToCreature();
            Player player = GetHitPlayer();
            if (honey == null || player == null) return;
            honey.GetAI().Talk(0, player);
            honey.CastSpell(player, 88425, false);
            honey.DespawnOrUnsummon();
        }
        public override void Register() => OnEffectHitTarget.Add(new(HandleScriptEffect, 0, SpellEffectName.ScriptEffect));
    }

    [Script]
    class spell_ruumbos_silly_dance : SpellScript
    {
        void HandleScriptEffect(int effIndex)
        {
            Player player = GetHitPlayer();
            if (player == null) return;
            player.SetEmoteState(Emote.StateDance);
            if (player.GetMapId() != 1) return;
            player.SummonCreature(47556, new Position(3852.52f, -1321.92f, 213.3353f, 5.72468f), TempSummonType.TimedDespawn, TimeSpan.FromSeconds(20));
            player.SummonCreature(47558, new Position(3850.44f, -1323.34f, 213.2113f, 5.637414f), TempSummonType.TimedDespawn, TimeSpan.FromSeconds(20));
        }
        public override void Register() => OnEffectHitTarget.Add(new(HandleScriptEffect, 0, SpellEffectName.ScriptEffect));
    }
}
