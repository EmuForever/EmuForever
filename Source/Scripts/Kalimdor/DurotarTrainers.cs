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
using static Scripts.Kalimdor.Durotar.DurotarData;

namespace Scripts.Kalimdor.Durotar
{
    [Script("npc_nortet", 0, QUEST_PROVING_PIT_WARRIOR)]
    [Script("npc_tunari", 1, QUEST_PROVING_PIT_PRIEST)]
    [Script("npc_seratha", 2, QUEST_PROVING_PIT_MAGE)]
    [Script("npc_legati", 3, QUEST_PROVING_PIT_ROGUE)]
    [Script("npc_nekali", 4, QUEST_PROVING_PIT_SHAMAN)]
    [Script("npc_ertezza", 5, QUEST_PROVING_PIT_HUNTER)]
    [Script("npc_zentabra", 6, QUEST_PROVING_PIT_DRUID)]
    [Script("npc_voldreka", 7, QUEST_PROVING_PIT_WARLOCK)]
    [Script("npc_zabrax", 8, QUEST_PROVING_PIT_MONK)]
    class npc_echo_isles_class_trainer : ScriptedAI
    {
        static readonly Position[] PitPoints =
        {
            new(-1158.99f, -5421.14f, 13.218976f, 0.2094395f),
            new(-1137, -5528.23f, 11.979752f, 3.1764990f),
            new(-1145.95f, -5543.13f, 12.48863f, 1.7278759f),
            new(-1146.67f, -5430.05f, 13.596256f, 1.4835298f),
            new(-1152.22f, -5407.6f, 13.263395f, 4.904375f),
            new(-1136.46f, -5525.13f, 11.99673f, 3.3161256f),
            new(-1158.81f, -5533.08f, 11.939185f, 0.3141593f),
            new(-1149.92f, -5407.46f, 13.235063f, 4.956735f),
            new(-1151.54f, -5429.86f, 13.29182f, 1.256637f)
        };
        readonly int _pitPos, _questId;
        bool _canMoveToPit = true;
        Position _initialHomePosition;
        public npc_echo_isles_class_trainer(Creature creature, int pitPos, int questId) : base(creature)
        {
            _pitPos = pitPos;
            _questId = questId;
        }
        public override void JustAppeared() => _initialHomePosition = new Position(me.GetPosition());
        public override void MovementInform(MovementGeneratorType type, int id)
        {
            if (type == MovementGeneratorType.Point && id == POINT_INITIAL_HOME) _canMoveToPit = true;
        }
        public override void OnQuestAccept(Player player, Quest quest)
        {
            if (quest.Id == _questId && _canMoveToPit)
            {
                _events.ScheduleEvent(EVENT_MOVE_TO_PIT, TimeSpan.FromSeconds(2));
                _canMoveToPit = false;
            }
        }
        public override void OnQuestReward(Player player, Quest quest, LootItemType type, int opt)
        {
            if (quest.Id == _questId) _events.RescheduleEvent(EVENT_MOVE_HOME, TimeSpan.FromSeconds(60));
        }
        public override bool OnGossipSelect(Player player, int menuId, int gossipListId)
        {
            if (menuId != GOSSIP_MENU_ARCANE_MOMENTUN) return false;
            player.CloseGossipMenu();
            switch (gossipListId)
            {
                case GOSSIP_OPTION_TRAIN_MOMENTUM: player.CastSpell(player, SPELL_LEARN_ARCANE_MOMENTUM, false); break;
                case GOSSIP_OPTION_UNTRAIN_MOMENTUM: player.CastSpell(player, SPELL_UNLEARN_ARCANE_MOMENTUM, false); break;
            }
            return true;
        }
        public override void UpdateAI(TimeSpan diff)
        {
            _events.Update(diff);
            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
                switch (eventId)
                {
                    case EVENT_MOVE_TO_PIT:
                        me.SetWalk(true);
                        Position pit = PitPoints[_pitPos];
                        me.GetMotionMaster().MovePoint(0, pit, true, pit.GetOrientation());
                        _events.ScheduleEvent(EVENT_MOVE_HOME, TimeSpan.FromSeconds(300));
                        break;
                    case EVENT_MOVE_HOME:
                        me.SetWalk(true);
                        me.GetMotionMaster().MovePoint(POINT_INITIAL_HOME, _initialHomePosition, true, _initialHomePosition.GetOrientation());
                        break;
                }
            UpdateVictim();
        }
    }

    [Script("quest_the_basics_hitting_things_warrior", NPC_TRAINER_NORTET, SAY_NOT_BAD)]
    [Script("quest_the_basics_hitting_things_priest", NPC_TRAINER_TUNARI, SAY_NOT_BAD)]
    [Script("quest_the_basics_hitting_things_mage", NPC_TRAINER_SERATHA, SAY_NOT_BAD)]
    [Script("quest_the_basics_hitting_things_rogue", NPC_TRAINER_LEGATI, SAY_NOT_BAD)]
    [Script("quest_the_basics_hitting_things_shaman", NPC_TRAINER_NEKALI, SAY_NOT_BAD)]
    [Script("quest_the_basics_hitting_things_hunter", NPC_TRAINER_ERTEZZA, SAY_NOT_BAD)]
    [Script("quest_the_basics_hitting_things_druid", NPC_TRAINER_ZENTABRA, SAY_NOT_BAD)]
    [Script("quest_the_basics_hitting_things_warlock", NPC_TRAINER_VOLDREKA, SAY_NOT_BAD)]
    [Script("quest_the_basics_hitting_things_monk", NPC_TRAINER_ZABRAX, SAY_NOT_BAD)]
    [Script("quest_proving_pit_warrior", NPC_TRAINER_NORTET, SAY_WELL_DONE)]
    [Script("quest_proving_pit_priest", NPC_TRAINER_TUNARI, SAY_WELL_DONE)]
    [Script("quest_proving_pit_mage", NPC_TRAINER_SERATHA, SAY_WELL_DONE)]
    [Script("quest_proving_pit_rogue", NPC_TRAINER_LEGATI, SAY_WELL_DONE)]
    [Script("quest_proving_pit_shaman", NPC_TRAINER_NEKALI, SAY_WELL_DONE)]
    [Script("quest_proving_pit_hunter", NPC_TRAINER_ERTEZZA, SAY_WELL_DONE)]
    [Script("quest_proving_pit_druid", NPC_TRAINER_ZENTABRA, SAY_WELL_DONE)]
    [Script("quest_proving_pit_warlock", NPC_TRAINER_VOLDREKA, SAY_WELL_DONE)]
    [Script("quest_proving_pit_monk", NPC_TRAINER_ZABRAX, SAY_WELL_DONE)]
    class quest_echo_isles_training : QuestScript
    {
        readonly int _trainerEntry, _textId;
        public quest_echo_isles_training(string name, int trainerEntry, int textId) : base(name)
        {
            _trainerEntry = trainerEntry;
            _textId = textId;
        }
        public override void OnQuestStatusChange(Player player, Quest quest, QuestStatus oldStatus, QuestStatus newStatus)
        {
            if (newStatus == QuestStatus.Complete)
                player.FindNearestCreature(_trainerEntry, 50, true)?.GetAI().Talk(_textId, player);
        }
    }
}
