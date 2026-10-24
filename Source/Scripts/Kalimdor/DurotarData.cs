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
namespace Scripts.Kalimdor.Durotar
{
    static class DurotarData
    {
        public const int SAY_SPELL_HIT = 0;
        public const int QUEST_LAZY_PEONS = 5441;
        public const int GO_LUMBERPILE = 175784;
        public const int SPELL_BUFF_SLEEP = 17743;
        public const int SPELL_AWAKEN_PEON = 19938;
        public const int SPELL_BREW = 16712;
        public const int SPELL_GHOSTLY = 16713;
        public const int SPELL_HEX1 = 16707;
        public const int SPELL_HEX2 = 16708;
        public const int SPELL_HEX3 = 16709;
        public const int SPELL_GROW = 16711;
        public const int SPELL_LAUNCH = 16716;
        public const int DATA_SHIP_DOCKED = 1;
        public const int GOSSIP_MENU_MITHAKA = 23225;
        public const int GOSSIP_TEXT_MITHAKA = 35969;
        public const int SPELL_ARCANE_MISSILES_TRAINER = 83470;
        public const int SPELL_TIKI_TARGET_VISUAL_1 = 71064;
        public const int SPELL_TIKI_TARGET_VISUAL_2 = 71065;
        public const int SPELL_TIKI_TARGET_VISUAL_3 = 71066;
        public const int SPELL_TIKI_TARGET_DEATH = 71240;
        public const int ACTION_EVENT_COMPLETE = 1;
        public const int ACTION_MOVE_TO_PRISONER = 1;
        public const int EVENT_PLAYER_ACCEPT_CHALLENGE = 1;
        public const int EVENT_WALK_BACK_TO_HOME = 2;
        public const int GOSSIP_JAILOR_EVENT_NOT_READY = 10973;
        public const int GOSSIP_JAILOR_EVENT_READY = 10974;
        public const int GOSSIP_EVENT_IN_PROGRESS_ID = 10973;
        public const int TEXT_GOSSIP_EVENT_IN_PROGRESS = 15252;
        public const int NPC_DARKSPEAR_JAILOR = 39062;
        public const int NPC_CAPTIVE_SPITESCALE_SCOUT = 38142;
        public const int PATH_CAGE_ONE = 30915500;
        public const int PATH_HOME_ONE = 30915501;
        public const int PATH_CAGE_TWO = 30908200;
        public const int PATH_HOME_TWO = 30908201;
        public const int SAY_GET_IN_THE_PIT = 0;
        public const int SPELL_ACTIVATE_DNT = 227105;
        public const int EVENT_UPDATE_JAILOR_GOSSIP = 1;
        public const int EVENT_TALK_TO_PLAYER = 2;
        public const int EVENT_DESPAWN_OUT_OF_COMBAT = 3;
        public const int EVENT_CAST_FROSTSHOCK = 4;
        public const int POINT_PRISONER_POSITION = 0;
        public const int SAY_SEND_YOU_TO_YOUR_DEATH = 0;
        public const int SPELL_FROST_SHOCK = 15089;
        public const int EVENT_MOVE_TO_PIT = 1;
        public const int EVENT_MOVE_HOME = 2;
        public const int GOSSIP_MENU_ARCANE_MOMENTUN = 20690;
        public const int GOSSIP_OPTION_TRAIN_MOMENTUM = 0;
        public const int GOSSIP_OPTION_UNTRAIN_MOMENTUM = 1;
        public const int NPC_TRAINER_NORTET = 38037;
        public const int NPC_TRAINER_TUNARI = 38245;
        public const int NPC_TRAINER_SERATHA = 38246;
        public const int NPC_TRAINER_LEGATI = 38244;
        public const int NPC_TRAINER_NEKALI = 38242;
        public const int NPC_TRAINER_ERTEZZA = 38247;
        public const int NPC_TRAINER_ZENTABRA = 38243;
        public const int NPC_TRAINER_VOLDREKA = 42618;
        public const int NPC_TRAINER_ZABRAX = 63310;
        public const int QUEST_PROVING_PIT_WARRIOR = 24642;
        public const int QUEST_PROVING_PIT_PRIEST = 24786;
        public const int QUEST_PROVING_PIT_MAGE = 24754;
        public const int QUEST_PROVING_PIT_ROGUE = 24774;
        public const int QUEST_PROVING_PIT_SHAMAN = 24762;
        public const int QUEST_PROVING_PIT_HUNTER = 24780;
        public const int QUEST_PROVING_PIT_DRUID = 24768;
        public const int QUEST_PROVING_PIT_WARLOCK = 26276;
        public const int QUEST_PROVING_PIT_MONK = 31161;
        public const int SPELL_LEARN_ARCANE_MOMENTUM = 232062;
        public const int SPELL_UNLEARN_ARCANE_MOMENTUM = 232063;
        public const int SAY_NOT_BAD = 0;
        public const int SAY_WELL_DONE = 1;
        public const int POINT_INITIAL_HOME = 1;
        public const int EVENT_VOLJIN_VISION_SCRIPT_1 = 1;
        public const int EVENT_VOLJIN_VISION_SCRIPT_2 = 2;
        public const int EVENT_VOLJIN_VISION_SCRIPT_3 = 3;
        public const int EVENT_VOLJIN_VISION_SCRIPT_4 = 4;
        public const int EVENT_VOLJIN_VISION_SCRIPT_5 = 5;
        public const int EVENT_VOLJIN_VISION_SCRIPT_6 = 6;
        public const int EVENT_VOLJIN_VISION_SCRIPT_7 = 7;
        public const int EVENT_VOLJIN_VISION_SCRIPT_8 = 8;
        public const int EVENT_VOLJIN_VISION_SCRIPT_9 = 9;
        public const int EVENT_VOLJIN_VISION_SCRIPT_10 = 10;
        public const int EVENT_VOLJIN_VISION_SCRIPT_11 = 11;
        public const int EVENT_VOLJIN_VISION_SCRIPT_12 = 12;
        public const int EVENT_VOLJIN_VISION_SCRIPT_13 = 13;
        public const int EVENT_VOLJIN_VISION_SCRIPT_14 = 14;
        public const int EVENT_VOLJIN_VISION_SCRIPT_15 = 15;
        public const int EVENT_VOLJIN_VISION_SCRIPT_16 = 16;
        public const int EVENT_VOLJIN_VISION_SCRIPT_17 = 17;
        public const int EVENT_VOLJIN_VISION_SCRIPT_18 = 18;
        public const int EVENT_VOLJIN_VISION_SCRIPT_19 = 19;
        public const int EVENT_VOLJIN_VISION_SCRIPT_20 = 20;
        public const int EVENT_VOLJIN_VISION_SCRIPT_21 = 21;
        public const int EVENT_VOLJIN_VISION_SCRIPT_22 = 22;
        public const int GOSSIP_MENU_GARROSH_VISION = 11112;
        public const int GOSSIP_MENU_THRALL_VISION = 11127;
        public const int GOSSIP_MENU_VISION_IN_PROGRESS = 11126;
        public const int QUEST_MORE_THAN_EXPECTED_WARRIOR = 24643;
        public const int QUEST_MORE_THAN_EXPECTED_MAGE = 24755;
        public const int QUEST_MORE_THAN_EXPECTED_SHAMAN = 24763;
        public const int QUEST_MORE_THAN_EXPECTED_ROGUE = 24775;
        public const int QUEST_MORE_THAN_EXPECTED_HUNTER = 24781;
        public const int QUEST_MORE_THAN_EXPECTED_PRIEST = 24787;
        public const int QUEST_MORE_THAN_EXPECTED_DRUID = 24769;
        public const int QUEST_MORE_THAN_EXPECTED_WARLOCK = 26277;
        public const int QUEST_MORE_THAN_EXPECTED_MONK = 31163;
        public const int QUEST_AN_ANCIENT_ENEMY = 24814;
        public const int SAY_GORROSH_VISION_SCRIPT_TEXT_0 = 0;
        public const int SAY_GORROSH_VISION_SCRIPT_TEXT_1 = 1;
        public const int SAY_GORROSH_VISION_SCRIPT_TEXT_2 = 2;
        public const int SAY_GORROSH_VISION_SCRIPT_TEXT_3 = 3;
        public const int SAY_GORROSH_VISION_SCRIPT_TEXT_4 = 4;
        public const int SAY_VOLJIN_VISION_SCRIPT_TEXT_0 = 0;
        public const int SAY_VOLJIN_VISION_SCRIPT_TEXT_1 = 1;
        public const int SAY_VOLJIN_VISION_SCRIPT_TEXT_2 = 2;
        public const int SAY_VOLJIN_VISION_SCRIPT_TEXT_3 = 3;
        public const int SAY_VOLJIN_VISION_SCRIPT_TEXT_4 = 4;
        public const int SAY_VOLJIN_VISION_SCRIPT_TEXT_5 = 5;
        public const int SAY_VOLJIN_VISION_SCRIPT_TEXT_6 = 6;
        public const int SAY_THRALL_VISION_SCRIPT_TEXT_0 = 0;
        public const int SAY_THRALL_VISION_SCRIPT_TEXT_1 = 1;
        public const int SAY_THRALL_VISION_SCRIPT_TEXT_2 = 2;
        public const int SAY_THRALL_VISION_SCRIPT_TEXT_3 = 3;
        public const int SAY_THRALL_VISION_SCRIPT_TEXT_4 = 4;
        public const int SAY_THRALL_VISION_SCRIPT_TEXT_5 = 5;
        public const int SAY_THRALL_VISION_SCRIPT_TEXT_6 = 6;
        public const int SAY_THRALL_VISION_SCRIPT_TEXT_7 = 7;
        public const int SAY_VOLJIN_SCRIPT_TEXT_0 = 0;
        public const int SAY_VOLJIN_SCRIPT_TEXT_1 = 1;
        public const int SAY_VOLJIN_SCRIPT_TEXT_2 = 2;
        public const int SAY_VOLJIN_SCRIPT_TEXT_3 = 3;
        public const int SAY_VOLJIN_SCRIPT_TEXT_4 = 4;
        public const int SAY_VOLJIN_SCRIPT_TEXT_5 = 5;
        public const int SAY_VOLJIN_SCRIPT_TEXT_6 = 6;
        public const int SAY_VOLJIN_SCRIPT_TEXT_7 = 7;
        public const int SAY_VOLJIN_SCRIPT_TEXT_8 = 8;
        public const int SAY_VOLJIN_SCRIPT_TEXT_9 = 9;
        public const int SAY_VOLJIN_SCRIPT_TEXT_10 = 10;
        public const int SAY_VOLJIN_SCRIPT_TEXT_11 = 11;
        public const int SAY_VOLJIN_SCRIPT_TEXT_12 = 12;
        public const int SAY_VOLJIN_SCRIPT_TEXT_13 = 13;
        public const int SAY_VOLJIN_SCRIPT_TEXT_14 = 14;
        public const int SPELL_RITES_OF_VISION = 73169;
        public const int SPELL_VOLJINS_VISION_SMOKE = 73158;
        public const int SPELL_GENERIC_QUEST_INVISIBILITY_1 = 49414;
        public const int SPELL_GENERIC_QUEST_INVISIBILITY_2 = 49415;
        public const int SPELL_GENERIC_QUEST_INVISIBILITY_8 = 78718;
    }
}
