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
using System;

namespace Scripts.Kalimdor.ThunderBluff
{
    [Script]
    class npc_cairne_bloodhoof : ScriptedAI
    {
        readonly TimeSpan[] _timers = new TimeSpan[5];
        static readonly int[] Spells = { 16636, 22916, 23931, 16856, 16044 };
        static readonly int[] InitialDelays = { 30000, 10000, 15000, 10000, 5000 };
        static readonly int[] RepeatDelays = { 25000, 20000, 15000, 15000, 7000 };
        public npc_cairne_bloodhoof(Creature creature) : base(creature) => Reset();
        public override void Reset()
        {
            for (int i = 0; i < _timers.Length; ++i) _timers[i] = TimeSpan.FromMilliseconds(InitialDelays[i]);
        }
        public override void JustEngagedWith(Unit who) => DoPlaySoundToSet(me, 5884);
        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim()) return;
            for (int i = 0; i < _timers.Length; ++i)
            {
                if (_timers[i] > diff) { _timers[i] -= diff; continue; }
                if (i == 0)
                {
                    Unit target = SelectTarget(SelectTargetMethod.Random, 0);
                    if (target != null) DoCast(target, Spells[i]);
                }
                else DoCastVictim(Spells[i]);
                _timers[i] = TimeSpan.FromMilliseconds(RepeatDelays[i]);
            }
        }
        public override bool OnGossipSelect(Player player, int menuId, int gossipListId)
        {
            int action = player.PlayerTalkClass.GetGossipOptionAction(gossipListId);
            player.ClearGossipMenu();
            if (action == eTradeskill.GossipSenderInfo)
            {
                player.CastSpell(player, 23123, false);
                player.SendGossipMenu(7014, me.GetGUID());
            }
            return true;
        }
        public override bool OnGossipHello(Player player)
        {
            player.InitGossipMenu(5851);
            if (me.IsQuestGiver()) player.PrepareQuestMenu(me.GetGUID());
            if (player.GetQuestStatus(925) == QuestStatus.Incomplete)
                player.AddGossipItem(5851, 0, eTradeskill.GossipSenderMain, eTradeskill.GossipSenderInfo);
            player.SendGossipMenu(7013, me.GetGUID());
            return true;
        }
    }
}
