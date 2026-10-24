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
using Game.Spells;
using System;
using static Scripts.Kalimdor.Durotar.DurotarData;

namespace Scripts.Kalimdor.Durotar
{
    [Script]
    class npc_darkspear_jailor : ScriptedAI
    {
        public npc_darkspear_jailor(Creature creature) : base(creature) { _pathCage = 0; _pathHome = 0;  }

        public override void JustAppeared()
        {
            me.SetGossipMenuId(GOSSIP_JAILOR_EVENT_READY);

            if (me.HasStringId("darkspear_jailor_one"))
            {
                _pathCage = PATH_CAGE_ONE;
                _pathHome = PATH_HOME_ONE;
            }
            else if (me.HasStringId("darkspear_jailor_two"))
            {
                _pathCage = PATH_CAGE_TWO;
                _pathHome = PATH_HOME_TWO;
            }
        }

        public override void DoAction(int param)
        {
            if (param == ACTION_EVENT_COMPLETE)
            {
                me.SetGossipMenuId(GOSSIP_JAILOR_EVENT_READY);
            }
        }

        public override void WaypointPathEnded(int nodeId, int pathId)
        {
            if (pathId == _pathCage)
            {
                me.HandleEmoteCommand(Emote.OneshotUseStanding);
                me.CastSpell(me, SPELL_ACTIVATE_DNT);
                _events.ScheduleEvent(EVENT_WALK_BACK_TO_HOME, TimeSpan.FromSeconds(1));
            }
        }

        public override bool OnGossipSelect(Player player, int menuId, int gossipListId)
        {
            player.CloseGossipMenu();
            player.KilledMonsterCredit(NPC_DARKSPEAR_JAILOR);
            Talk(SAY_GET_IN_THE_PIT, player);
            me.SetGossipMenuId(GOSSIP_JAILOR_EVENT_NOT_READY);
            _events.ScheduleEvent(EVENT_PLAYER_ACCEPT_CHALLENGE, TimeSpan.FromSeconds(2));
            return true;
        }

        public override void UpdateAI(TimeSpan diff)
        {
            _events.Update(diff);

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_PLAYER_ACCEPT_CHALLENGE:
                        me.GetMotionMaster().MovePath(_pathCage, false);
                        break;
                    case EVENT_WALK_BACK_TO_HOME:
                        if (me.FindNearestCreature(NPC_CAPTIVE_SPITESCALE_SCOUT, 5.0f, true) is Creature scout)
                            scout.GetAI().DoAction(ACTION_MOVE_TO_PRISONER);
                        me.GetMotionMaster().MovePath(_pathHome, false);
                        break;
                    default:
                        break;
                }
            }

            if (!UpdateVictim())
                return;
        }

        int _pathCage;
        int _pathHome;
    }

    [Script]
    class npc_captive_spitescale_scout : ScriptedAI
    {
        public npc_captive_spitescale_scout(Creature creature) : base(creature) { }

        public override void JustAppeared()
        {
            _events.ScheduleEvent(EVENT_UPDATE_JAILOR_GOSSIP, TimeSpan.FromSeconds(2));
        }

        public override void JustDied(Unit who)
        {
            me.SetImmuneToPC(true);
            me.DespawnOrUnsummon(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(7));
        }

        public override void EnterEvadeMode(EvadeReason why = EvadeReason.Other)
        {
            me.DespawnOrUnsummon(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2));
        }

        public override void JustEngagedWith(Unit who)
        {
            _events.CancelEvent(EVENT_DESPAWN_OUT_OF_COMBAT);
            _events.ScheduleEvent(EVENT_CAST_FROSTSHOCK, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        }

        public override void DoAction(int param)
        {
            if (param == ACTION_MOVE_TO_PRISONER)
            {
                me.SetWalk(true);

                if (me.HasStringId("captive_spitescale_scout_one"))
                    me.GetMotionMaster().MovePoint(POINT_PRISONER_POSITION, PrisonerPositionOne);
                else if (me.HasStringId("captive_spitescale_scout_two"))
                    me.GetMotionMaster().MovePoint(POINT_PRISONER_POSITION, PrisonerPositionTwo);
            }
        }

        public override void MovementInform(MovementGeneratorType type, int id)
        {
            if (type == MovementGeneratorType.Point && id == POINT_PRISONER_POSITION)
            {
                _events.ScheduleEvent(EVENT_TALK_TO_PLAYER, TimeSpan.FromSeconds(1));
                _events.ScheduleEvent(EVENT_DESPAWN_OUT_OF_COMBAT, TimeSpan.FromSeconds(100));
            }
        }

        public override void UpdateAI(TimeSpan diff)
        {
            _events.Update(diff);

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_UPDATE_JAILOR_GOSSIP:
                        if (me.FindNearestCreature(NPC_DARKSPEAR_JAILOR, 25.0f, true) is Creature jailer)
                            jailer.GetAI().DoAction(ACTION_EVENT_COMPLETE);
                        break;
                    case EVENT_TALK_TO_PLAYER:
                        Talk(SAY_SEND_YOU_TO_YOUR_DEATH);
                        me.SetImmuneToPC(false);
                        break;
                    case EVENT_DESPAWN_OUT_OF_COMBAT:
                        me.DespawnOrUnsummon(TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(2));
                        break;
                    case EVENT_CAST_FROSTSHOCK:
                        DoCastVictim(SPELL_FROST_SHOCK);
                        break;
                    default:
                        break;
                }
            }

            if (!UpdateVictim())
                return;
        }

        static readonly Position PrisonerPositionOne = new(-1142.49f, -5415.59f, 10.597655f);
        static readonly Position PrisonerPositionTwo = new(-1149.03f, -5526.18f, 8.1045685f);
    }
}
