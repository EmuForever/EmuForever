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
    class npc_voljin_garrosh_vision : ScriptedAI
    {
        public npc_voljin_garrosh_vision(Creature creature) : base(creature) { _scriptRunning = false;  }

        public override void JustAppeared()
        {
            me.SetGossipMenuId(GOSSIP_MENU_GARROSH_VISION);

            Creature garrosh = me.FindNearestCreatureWithOptions(10.0f, new() { StringId = "vision_of_garrosh", IgnorePhases = true });
            Creature voljin = me.FindNearestCreatureWithOptions(10.0f, new() { StringId = "vision_of_voljin", IgnorePhases = true });
            Creature bunny = me.FindNearestCreatureWithOptions(10.0f, new() { StringId = "vision_brazier_bunny_garrosh", IgnorePhases = true });
            GameObject brazier = me.FindNearestGameObjectWithOptions(10.0f, new() { StringId = "vision_brazier_garrosh_voljin", IgnorePhases = true });

            if (garrosh == null || voljin == null || bunny == null || brazier == null)
                return;

            _garroshGUID = garrosh.GetGUID();
            _voljinGUID = voljin.GetGUID();
            _bunnyGUID = bunny.GetGUID();
            _brazierGUID = brazier.GetGUID();
        }

        public override void OnQuestReward(Player player, Quest quest, LootItemType type, int opt)
        {
            if (_scriptRunning)
                return;

            switch (quest.Id)
            {
                case QUEST_MORE_THAN_EXPECTED_WARRIOR:
                case QUEST_MORE_THAN_EXPECTED_MAGE:
                case QUEST_MORE_THAN_EXPECTED_SHAMAN:
                case QUEST_MORE_THAN_EXPECTED_ROGUE:
                case QUEST_MORE_THAN_EXPECTED_HUNTER:
                case QUEST_MORE_THAN_EXPECTED_PRIEST:
                case QUEST_MORE_THAN_EXPECTED_DRUID:
                case QUEST_MORE_THAN_EXPECTED_WARLOCK:
                case QUEST_MORE_THAN_EXPECTED_MONK:
                    _scriptRunning = true;
                    _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_1, TimeSpan.FromSeconds(0));
                    break;
                default:
                    break;
            }
        }

        public override bool OnGossipSelect(Player player, int menuId, int gossipListId)
        {
            player.CloseGossipMenu();
            _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_1, TimeSpan.FromSeconds(0));
            _scriptRunning = true;
            return false;
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!_scriptRunning)
                return;

            _events.Update(diff);

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_VOLJIN_VISION_SCRIPT_1:
                    {
                        me.SetGossipMenuId(GOSSIP_MENU_VISION_IN_PROGRESS);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_2, TimeSpan.FromSeconds(0));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_2:
                    {
                        me.SetFacingTo(3.97935f);
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_0);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_3, TimeSpan.FromSeconds(3));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_3:
                    {
                        DoCastSelf(SPELL_RITES_OF_VISION);
                        if (ObjectAccessor.GetCreature(me, _bunnyGUID) is Creature bunny)
                            bunny.GetAI().DoCastSelf(SPELL_VOLJINS_VISION_SMOKE);
                        if (ObjectAccessor.GetGameObject(me, _brazierGUID) is GameObject brazier)
                            brazier.SetGoState(GameObjectState.Active);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_4, TimeSpan.FromSeconds(4));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_4:
                    {
                        if (ObjectAccessor.GetCreature(me, _garroshGUID) is Creature garrosh)
                        {
                            garrosh.GetAI().DoCastSelf(SPELL_GENERIC_QUEST_INVISIBILITY_1);
                            garrosh.RemoveAura(SPELL_GENERIC_QUEST_INVISIBILITY_8);
                        }

                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_5, TimeSpan.FromSeconds(1));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_5:
                    {
                        if (ObjectAccessor.GetCreature(me, _garroshGUID) is Creature garrosh)
                            garrosh.GetAI().Talk(SAY_GORROSH_VISION_SCRIPT_TEXT_0);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_6, TimeSpan.FromSeconds(7));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_6:
                    {
                        if (ObjectAccessor.GetCreature(me, _voljinGUID) is Creature voljin)
                        {
                            voljin.GetAI().DoCastSelf(SPELL_GENERIC_QUEST_INVISIBILITY_1);
                            voljin.RemoveAura(SPELL_GENERIC_QUEST_INVISIBILITY_8);
                        }
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_7, TimeSpan.FromSeconds(4));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_7:
                    {
                        if (ObjectAccessor.GetCreature(me, _voljinGUID) is Creature voljin)
                            voljin.GetAI().Talk(SAY_VOLJIN_VISION_SCRIPT_TEXT_0);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_8, TimeSpan.FromSeconds(11));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_8:
                    {
                        if (ObjectAccessor.GetCreature(me, _voljinGUID) is Creature voljin)
                            voljin.GetAI().Talk(SAY_VOLJIN_VISION_SCRIPT_TEXT_1);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_9, TimeSpan.FromSeconds(6));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_9:
                    {
                        if (ObjectAccessor.GetCreature(me, _garroshGUID) is Creature garrosh)
                            garrosh.GetAI().Talk(SAY_GORROSH_VISION_SCRIPT_TEXT_1);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_10, TimeSpan.FromSeconds(11));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_10:
                    {
                        if (ObjectAccessor.GetCreature(me, _voljinGUID) is Creature voljin)
                            voljin.GetAI().Talk(SAY_VOLJIN_VISION_SCRIPT_TEXT_2);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_11, TimeSpan.FromSeconds(11));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_11:
                    {
                        if (ObjectAccessor.GetCreature(me, _garroshGUID) is Creature garrosh)
                            garrosh.GetAI().Talk(SAY_GORROSH_VISION_SCRIPT_TEXT_2);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_12, TimeSpan.FromSeconds(16));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_12:
                    {
                        if (ObjectAccessor.GetCreature(me, _voljinGUID) is Creature voljin)
                            voljin.GetAI().Talk(SAY_VOLJIN_VISION_SCRIPT_TEXT_3);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_13, TimeSpan.FromSeconds(16));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_13:
                    {
                        if (ObjectAccessor.GetCreature(me, _voljinGUID) is Creature voljin)
                            voljin.GetAI().Talk(SAY_VOLJIN_VISION_SCRIPT_TEXT_4);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_14, TimeSpan.FromSeconds(15));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_14:
                    {
                        if (ObjectAccessor.GetCreature(me, _voljinGUID) is Creature voljin)
                            voljin.GetAI().Talk(SAY_VOLJIN_VISION_SCRIPT_TEXT_5);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_15, TimeSpan.FromSeconds(17));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_15:
                    {
                        if (ObjectAccessor.GetCreature(me, _garroshGUID) is Creature garrosh)
                            garrosh.GetAI().Talk(SAY_GORROSH_VISION_SCRIPT_TEXT_3);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_16, TimeSpan.FromSeconds(3));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_16:
                    {
                        if (ObjectAccessor.GetCreature(me, _garroshGUID) is Creature garrosh)
                            garrosh.GetAI().Talk(SAY_GORROSH_VISION_SCRIPT_TEXT_4);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_17, TimeSpan.FromSeconds(3));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_17:
                    {
                        if (ObjectAccessor.GetCreature(me, _voljinGUID) is Creature voljin)
                            voljin.GetAI().Talk(SAY_VOLJIN_VISION_SCRIPT_TEXT_6);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_18, TimeSpan.FromSeconds(9));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_18:
                    {
                        me.RemoveAura(SPELL_RITES_OF_VISION);
                        if (ObjectAccessor.GetCreature(me, _garroshGUID) is Creature garrosh)
                        {
                            garrosh.GetAI().DoCastSelf(SPELL_GENERIC_QUEST_INVISIBILITY_8);
                            garrosh.RemoveAura(SPELL_GENERIC_QUEST_INVISIBILITY_1);
                        }

                        if (ObjectAccessor.GetCreature(me, _voljinGUID) is Creature voljin)
                        {
                            voljin.GetAI().DoCastSelf(SPELL_GENERIC_QUEST_INVISIBILITY_8);
                            voljin.RemoveAura(SPELL_GENERIC_QUEST_INVISIBILITY_1);
                        }
                        if (ObjectAccessor.GetCreature(me, _bunnyGUID) is Creature bunny)
                            bunny.RemoveAura(SPELL_VOLJINS_VISION_SMOKE);
                        if (ObjectAccessor.GetGameObject(me, _brazierGUID) is GameObject brazier)
                            brazier.SetGoState(GameObjectState.Ready);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_19, TimeSpan.FromSeconds(2));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_19:
                    {
                        me.SetFacingTo(0.837758f);
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_1);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_20, TimeSpan.FromSeconds(9));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_20:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_2);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_21, TimeSpan.FromSeconds(16));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_21:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_3);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_22, TimeSpan.FromSeconds(8));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_22:
                    {
                        me.SetGossipMenuId(GOSSIP_MENU_GARROSH_VISION);
                        _scriptRunning = false;
                        break;
                    }
                    default:
                        break;
                }
            }
        }

        bool _scriptRunning;
        ObjectGuid _garroshGUID;
        ObjectGuid _voljinGUID;
        ObjectGuid _bunnyGUID;
        ObjectGuid _brazierGUID;
    }

    [Script]
    class npc_voljin_thrall_vision : ScriptedAI
    {
        public npc_voljin_thrall_vision(Creature creature) : base(creature) { _scriptRunning = false;  }

        public override void JustAppeared()
        {
            me.SetGossipMenuId(GOSSIP_MENU_THRALL_VISION);

            Creature thrall = me.FindNearestCreatureWithOptions(10.0f, new() { StringId = "vision_of_thrall", IgnorePhases = true });
            Creature bunny = me.FindNearestCreatureWithOptions(10.0f, new() { StringId = "vision_brazier_bunny_thrall", IgnorePhases = true });
            GameObject brazier = me.FindNearestGameObjectWithOptions(10.0f, new() { StringId = "vision_brazier_thrall", IgnorePhases = true });

            if (thrall == null || bunny == null || brazier == null)
                return;

            _thrallGUID = thrall.GetGUID();
            _bunnyGUID = bunny.GetGUID();
            _brazierGUID = brazier.GetGUID();
        }

        public override void OnQuestReward(Player player, Quest quest, LootItemType type, int opt)
        {
            if (_scriptRunning)
                return;

            if (quest.Id == QUEST_AN_ANCIENT_ENEMY)
            {
                _scriptRunning = true;
                _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_1, TimeSpan.FromSeconds(0));
            }
        }

        public override bool OnGossipSelect(Player player, int menuId, int gossipListId)
        {
            _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_1, TimeSpan.FromSeconds(0));
            player.CloseGossipMenu();
            _scriptRunning = true;
            return false;
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!_scriptRunning)
                return;

            _events.Update(diff);

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_VOLJIN_VISION_SCRIPT_1:
                    {
                        me.SetGossipMenuId(GOSSIP_MENU_VISION_IN_PROGRESS);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_2, TimeSpan.FromSeconds(0));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_2:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_4);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_3, TimeSpan.FromSeconds(9));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_3:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_5);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_4, TimeSpan.FromSeconds(8));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_4:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_6);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_5, TimeSpan.FromSeconds(7));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_5:
                    {
                        DoCastSelf(SPELL_RITES_OF_VISION);
                        me.SetFacingTo(3.97935f);
                        if (ObjectAccessor.GetCreature(me, _bunnyGUID) is Creature bunny)
                            bunny.GetAI().DoCastSelf(SPELL_VOLJINS_VISION_SMOKE);
                        if (ObjectAccessor.GetGameObject(me, _brazierGUID) is GameObject brazier)
                            brazier.SetGoState(GameObjectState.Active);
                        if (ObjectAccessor.GetCreature(me, _thrallGUID) is Creature thrall)
                        {
                            thrall.GetAI().DoCastSelf(SPELL_GENERIC_QUEST_INVISIBILITY_2);
                            thrall.RemoveAura(SPELL_GENERIC_QUEST_INVISIBILITY_8);
                        }
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_6, TimeSpan.FromSeconds(5));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_6:
                    {
                        if (ObjectAccessor.GetCreature(me, _thrallGUID) is Creature thrall)
                            thrall.GetAI().Talk(SAY_THRALL_VISION_SCRIPT_TEXT_0);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_7, TimeSpan.FromSeconds(4));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_7:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_7);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_8, TimeSpan.FromSeconds(6));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_8:
                    {
                        if (ObjectAccessor.GetCreature(me, _thrallGUID) is Creature thrall)
                            thrall.GetAI().Talk(SAY_THRALL_VISION_SCRIPT_TEXT_1);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_9, TimeSpan.FromSeconds(11));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_9:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_8);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_10, TimeSpan.FromSeconds(18));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_10:
                    {
                        if (ObjectAccessor.GetCreature(me, _thrallGUID) is Creature thrall)
                            thrall.GetAI().Talk(SAY_THRALL_VISION_SCRIPT_TEXT_2);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_11, TimeSpan.FromSeconds(6));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_11:
                    {
                        if (ObjectAccessor.GetCreature(me, _thrallGUID) is Creature thrall)
                            thrall.GetAI().Talk(SAY_THRALL_VISION_SCRIPT_TEXT_3);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_12, TimeSpan.FromSeconds(12));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_12:
                    {
                        if (ObjectAccessor.GetCreature(me, _thrallGUID) is Creature thrall)
                            thrall.GetAI().Talk(SAY_THRALL_VISION_SCRIPT_TEXT_4);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_13, TimeSpan.FromSeconds(18));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_13:
                    {
                        if (ObjectAccessor.GetCreature(me, _thrallGUID) is Creature thrall)
                            thrall.GetAI().Talk(SAY_THRALL_VISION_SCRIPT_TEXT_5);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_14, TimeSpan.FromSeconds(17));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_14:
                    {
                        if (ObjectAccessor.GetCreature(me, _thrallGUID) is Creature thrall)
                            thrall.GetAI().Talk(SAY_THRALL_VISION_SCRIPT_TEXT_6);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_15, TimeSpan.FromSeconds(9));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_15:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_9);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_16, TimeSpan.FromSeconds(11));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_16:
                    {
                        if (ObjectAccessor.GetCreature(me, _thrallGUID) is Creature thrall)
                            thrall.GetAI().Talk(SAY_THRALL_VISION_SCRIPT_TEXT_7);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_17, TimeSpan.FromSeconds(6));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_17:
                    {
                        me.RemoveAura(SPELL_RITES_OF_VISION);
                        me.SetFacingTo(0.837758f);
                        if (ObjectAccessor.GetCreature(me, _thrallGUID) is Creature thrall)
                        {
                            thrall.GetAI().DoCastSelf(SPELL_GENERIC_QUEST_INVISIBILITY_8);
                            thrall.RemoveAura(SPELL_GENERIC_QUEST_INVISIBILITY_2);
                        }
                        if (ObjectAccessor.GetCreature(me, _bunnyGUID) is Creature bunny)
                            bunny.RemoveAura(SPELL_VOLJINS_VISION_SMOKE);
                        if (ObjectAccessor.GetGameObject(me, _brazierGUID) is GameObject brazier)
                            brazier.SetGoState(GameObjectState.Ready);
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_10);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_18, TimeSpan.FromSeconds(8));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_18:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_11);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_19, TimeSpan.FromSeconds(9));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_19:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_12);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_20, TimeSpan.FromSeconds(17));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_20:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_13);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_21, TimeSpan.FromSeconds(11));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_21:
                    {
                        Talk(SAY_VOLJIN_SCRIPT_TEXT_14);
                        _events.ScheduleEvent(EVENT_VOLJIN_VISION_SCRIPT_22, TimeSpan.FromSeconds(11));
                        break;
                    }
                    case EVENT_VOLJIN_VISION_SCRIPT_22:
                    {
                        me.SetGossipMenuId(GOSSIP_MENU_THRALL_VISION);
                        _scriptRunning = false;
                        break;
                    }
                    default:
                        break;
                }
            }
        }

        bool _scriptRunning;
        ObjectGuid _thrallGUID;
        ObjectGuid _bunnyGUID;
        ObjectGuid _brazierGUID;
    }
}
