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
using Game;
using Game.AI;
using Game.Entities;
using Game.Scripting;
using System;

namespace Scripts.Kalimdor.Barrens
{
    [Script]
    class npc_wizzlecrank_shredder : EscortAI
    {
        bool _postEvent;
        TimeSpan _postEventTimer = TimeSpan.FromSeconds(1);
        int _postEventCount;
        public npc_wizzlecrank_shredder(Creature creature) : base(creature) => me.SetReactState(ReactStates.Defensive);
        public override void WaypointReached(int waypointId, int pathId)
        {
            switch (waypointId)
            {
                case 0: Talk(1); break;
                case 17:
                    Creature mercenary = me.SummonCreature(3282, new Position(1128.489f, -3037.611f, 92.701f, 1.472f), TempSummonType.TimedDespawnOutOfCombat, TimeSpan.FromMinutes(2));
                    if (mercenary != null)
                    {
                        mercenary.GetAI().Talk(0);
                        me.SummonCreature(3282, new Position(1160.172f, -2980.168f, 97.313f, 3.690f), TempSummonType.TimedDespawnOutOfCombat, TimeSpan.FromMinutes(2));
                    }
                    break;
                case 24: _postEvent = true; break;
            }
        }
        public override void WaypointStarted(int pointId, int pathId)
        {
            Player player = GetPlayerForEscort();
            if (player == null) return;
            switch (pointId)
            {
                case 9: Talk(2, player); break;
                case 18: Talk(3, player); break;
            }
        }
        public override void JustSummoned(Creature summoned)
        {
            if (summoned.GetEntry() == 3451) me.SetStandState(UnitStandStateType.Dead);
            if (summoned.GetEntry() == 3282) summoned.GetAI().AttackStart(me);
        }
        public override void UpdateEscortAI(TimeSpan diff)
        {
            if (UpdateVictim() || !_postEvent) return;
            if (_postEventTimer > diff) { _postEventTimer -= diff; return; }
            switch (_postEventCount++)
            {
                case 0: Talk(4); break;
                case 1: Talk(5); break;
                case 2: Talk(6); break;
                case 3:
                    Player player = GetPlayerForEscort();
                    if (player != null)
                    {
                        player.GroupEventHappens(863, me);
                        me.DespawnOrUnsummon(TimeSpan.FromMinutes(5));
                        me.SummonCreature(3451, new Position(0, 0, 0, 0), TempSummonType.TimedDespawn, TimeSpan.FromMinutes(3));
                    }
                    break;
            }
            _postEventTimer = TimeSpan.FromSeconds(5);
        }
        public override void OnQuestAccept(Player player, Quest quest)
        {
            if (quest.Id != 863) return;
            me.SetFaction(637);
            Talk(0);
            SetDespawnAtEnd(false);
            LoadPath(27514);
            Start(true, player.GetGUID());
        }
    }
}
