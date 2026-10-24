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
using Game.Entities;
namespace Scripts.Kalimdor.Winterspring
{
    static class WinterspringData
    {
        public const int SAY_QUEST_START = 0;
        public const int SAY_ENTER_OWL_THICKET = 1;
        public const int SAY_REACH_TORCH = 2;
        public const int SAY_AFTER_TORCH = 3;
        public const int SAY_REACH_ALTAR_1 = 4;
        public const int SAY_REACH_ALTAR_2 = 5;
        public const int SAY_RANSHALLA_ALTAR_1 = 6;
        public const int SAY_RANSHALLA_ALTAR_2 = 7;
        public const int SAY_PRIESTESS_ALTAR_3 = 8;
        public const int SAY_PRIESTESS_ALTAR_4 = 9;
        public const int SAY_RANSHALLA_ALTAR_5 = 10;
        public const int SAY_RANSHALLA_ALTAR_6 = 11;
        public const int SAY_PRIESTESS_ALTAR_7 = 12;
        public const int SAY_PRIESTESS_ALTAR_8 = 13;
        public const int SAY_PRIESTESS_ALTAR_9 = 14;
        public const int SAY_PRIESTESS_ALTAR_10 = 15;
        public const int SAY_PRIESTESS_ALTAR_11 = 16;
        public const int SAY_PRIESTESS_ALTAR_12 = 17;
        public const int SAY_PRIESTESS_ALTAR_13 = 18;
        public const int SAY_PRIESTESS_ALTAR_14 = 19;
        public const int SAY_VOICE_ALTAR_15 = 20;
        public const int SAY_PRIESTESS_ALTAR_16 = 21;
        public const int SAY_PRIESTESS_ALTAR_17 = 22;
        public const int SAY_PRIESTESS_ALTAR_18 = 23;
        public const int SAY_PRIESTESS_ALTAR_19 = 24;
        public const int SAY_PRIESTESS_ALTAR_20 = 25;
        public const int SAY_PRIESTESS_ALTAR_21 = 26;
        public const int SAY_RANSHALLA_END_1 = 27;
        public const int SAY_RANSHALLA_END_2 = 28;
        public const int EMOTE_CHANT_SPELL = 29;
        public const int SPELL_LIGHT_TORCH = 18953;
        public const int NPC_RANSHALLA = 10300;
        public const int NPC_PRIESTESS_ELUNE = 12116;
        public const int NPC_VOICE_ELUNE = 12152;
        public const int NPC_GUARDIAN_ELUNE = 12140;
        public const int GO_ELUNE_ALTAR = 177404;
        public const int GO_ELUNE_FIRE = 177417;
        public const int GO_ELUNE_GEM = 177414;
        public const int GO_ELUNE_LIGHT = 177415;
        public const int QUEST_GUARDIANS_ALTAR = 4901;
        public const int NPC_PRIESTESS_DATA_1 = -1;
        public const int NPC_PRIESTESS_DATA_2 = -2;
        public const int DATA_MOVE_PRIESTESS = -3;
        public const int DATA_EVENT_END = -4;
        public const int EVENT_RESUME = 1;
        public const int PATH_ESCORT_RANSHALLA = 82402;
        public static readonly (int Text, int Speaker, int Delay)[] IntroDialogue =
        {
            (SAY_REACH_ALTAR_1,         NPC_RANSHALLA,          2000),
            (SAY_REACH_ALTAR_2,         NPC_RANSHALLA,          3000),
            (NPC_RANSHALLA,             0,                         0),
            (SAY_PRIESTESS_ALTAR_3,     NPC_PRIESTESS_DATA_2,   1000),
            (SAY_PRIESTESS_ALTAR_4,     NPC_PRIESTESS_DATA_1,   4000),
            (SAY_RANSHALLA_ALTAR_5,     NPC_RANSHALLA,          4000),
            (SAY_RANSHALLA_ALTAR_6,     NPC_RANSHALLA,          4000),
            (SAY_PRIESTESS_ALTAR_7,     NPC_PRIESTESS_DATA_2,   4000),
            (SAY_PRIESTESS_ALTAR_8,     NPC_PRIESTESS_DATA_2,   5000),
            (GO_ELUNE_GEM,              0,                      5000),
            (SAY_PRIESTESS_ALTAR_9,     NPC_PRIESTESS_DATA_1,   4000),
            (NPC_PRIESTESS_DATA_1,      0,                      3000),
            (SAY_PRIESTESS_ALTAR_10,    NPC_PRIESTESS_DATA_1,   5000),
            (SAY_PRIESTESS_ALTAR_11,    NPC_PRIESTESS_DATA_1,   4000),
            (SAY_PRIESTESS_ALTAR_12,    NPC_PRIESTESS_DATA_1,   5000),
            (SAY_PRIESTESS_ALTAR_13,    NPC_PRIESTESS_DATA_1,   8000),
            (NPC_VOICE_ELUNE,           0,                     12000),
            (SAY_VOICE_ALTAR_15,        NPC_VOICE_ELUNE,        5000),
            (NPC_PRIESTESS_DATA_2,      0,                      3000),
            (SAY_PRIESTESS_ALTAR_16,    NPC_PRIESTESS_DATA_2,   4000),
            (SAY_PRIESTESS_ALTAR_17,    NPC_PRIESTESS_DATA_2,   6000),
            (SAY_PRIESTESS_ALTAR_18,    NPC_PRIESTESS_DATA_1,   5000),
            (SAY_PRIESTESS_ALTAR_19,    NPC_PRIESTESS_DATA_1,   3000),
            (NPC_GUARDIAN_ELUNE,        0,                      2000),
            (SAY_PRIESTESS_ALTAR_20,    NPC_PRIESTESS_DATA_1,   4000),
            (SAY_PRIESTESS_ALTAR_21,    NPC_PRIESTESS_DATA_2,  10000),
            (DATA_MOVE_PRIESTESS,       0,                      6000),
            (DATA_EVENT_END,            0,                      2000),
            (SAY_RANSHALLA_END_2,       NPC_RANSHALLA, 0),
            (0,                         0,                         0),
        };
        public static readonly Position[] WingThicketLocations =
        {
            new(5515.98f, -4903.43f, 846.30f, 4.58f),
            new(5501.94f, -4920.20f, 848.69f, 6.15f),
            new(5497.35f, -4906.49f, 850.83f, 2.76f),
            new(5518.38f, -4913.47f, 845.57f, 0.00f),
            new(5510.36f, -4921.17f, 846.33f, 0.00f),
            new(5511.31f, -4913.82f, 847.17f, 0.00f),
            new(5518.51f, -4917.56f, 845.23f, 0.00f),
            new(5514.40f, -4921.16f, 845.49f, 0.00f),
        };
    }
}
