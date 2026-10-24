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
namespace Scripts.Kalimdor.ZulFarrak
{
    static class ZulFarrakData
    {
        public const int BOSS_HYDROMANCER_VELRATHA = 0;
        public const int BOSS_GAHZ_RILLA = 1;
        public const int BOSS_ANTU_SUL = 2;
        public const int BOSS_THEKA_THE_MARTYR = 3;
        public const int BOSS_WITCH_DOCTOR_ZUM_RAH = 4;
        public const int BOSS_NEKRUM_GUTCHEWER = 5;
        public const int BOSS_SHADOWPRIEST_SEZZ_ZIZ = 6;
        public const int BOSS_CHIEF_UKORZ_SANDSCALP = 7;
        public const int MAX_ENCOUNTER = 8;
        public const int ENTRY_SANDSCALP = 7267;
        public const int ENTRY_ZUM_RAH = 7271;
        public const int ENTRY_THEKA = 7272;
        public const int ENTRY_GAHZRILLA = 7273;
        public const int ENTRY_SEZZZIZ = 7275;
        public const int ENTRY_BLY = 7604;
        public const int ENTRY_RAVEN = 7605;
        public const int ENTRY_ORO = 7606;
        public const int ENTRY_WEEGLI = 7607;
        public const int ENTRY_MURTA = 7608;
        public const int ENTRY_VELRTHA = 7795;
        public const int ENTRY_NEKRUM = 7796;
        public const int ENTRY_ANTUSUL = 8127;
        public const int GO_END_DOOR = 146084;
        public const int EVENT_PYRAMID = 1;
        public const int EVENT_GAHZRILLA = 2;
        public const int PYRAMID_NOT_STARTED = 0;
        public const int PYRAMID_CAGES_OPEN = 1;
        public const int PYRAMID_ARRIVED_AT_STAIR = 2;
        public const int PYRAMID_WAVE_1 = 3;
        public const int PYRAMID_PRE_WAVE_2 = 4;
        public const int PYRAMID_WAVE_2 = 5;
        public const int PYRAMID_PRE_WAVE_3 = 6;
        public const int PYRAMID_WAVE_3 = 7;
        public const int PYRAMID_KILLED_ALL_TROLLS = 8;
        public const int NPC_GAHZRILLA = 7273;
        public const int PATH_ADDS = 652424;
        public const int SAY_SANCT_INVADE = 0;
        public const int SAY_WARD = 1;
        public const int SAY_KILL = 2;
        public const int SPELL_SHADOW_BOLT = 12739;
        public const int SPELL_SHADOWBOLT_VOLLEY = 15245;
        public const int SPELL_WARD_OF_ZUM_RAH = 11086;
        public const int SPELL_HEALING_WAVE = 12491;
        public const int EVENT_SHADOW_BOLT = 1;
        public const int EVENT_SHADOWBOLT_VOLLEY = 2;
        public const int EVENT_WARD_OF_ZUM_RAH = 3;
        public const int EVENT_HEALING_WAVE = 4;
        public const int SAY_1 = 0;
        public const int SAY_2 = 1;
        public const int SPELL_SHIELD_BASH = 11972;
        public const int SPELL_REVENGE = 12170;
        public const int GOSSIP_BLY_MID = 941;
        public const int GOSSIP_BLY_OID = 1;
        public const int SPELL_BOMB = 8858;
        public const int SPELL_GOBLIN_LAND_MINE = 21688;
        public const int SPELL_SHOOT = 6660;
        public const int SPELL_WEEGLIS_BARREL = 10772;
        public const int SAY_WEEGLI_OHNO = 0;
        public const int SAY_WEEGLI_OK_I_GO = 1;
        public const int GOSSIP_WEEGLI_MID = 940;
        public const int GOSSIP_WEEGLI_OID = 0;
        public const int NPC_ZOMBIE = 7286;
        public const int NPC_DEAD_HERO = 7276;
        public const int CHANCE_ZOMBIE = 65;
        public const int CHANCE_DEAD_HERO = 10;
        public const int ZUMRAH_ID = 7271;
        public static readonly (int Wave, int Entry, float X, float Y)[] PyramidSpawns =
        {
            (1, 7789, 1894.64f, 1206.29f),
            (1, 7787, 1890.08f, 1218.68f),
            (1, 8876, 1883.76f, 1222.3f),
            (1, 7789, 1874.18f, 1221.24f),
            (1, 7787, 1892.28f, 1225.49f),
            (1, 7788, 1889.94f, 1212.21f),
            (1, 7787, 1879.02f, 1223.06f),
            (1, 7789, 1874.45f, 1204.44f),
            (1, 8876, 1898.23f, 1217.97f),
            (1, 7787, 1882.07f, 1225.7f),
            (1, 8877, 1896.46f, 1205.62f),
            (1, 7787, 1886.97f, 1225.86f),
            (1, 7787, 1894.72f, 1221.91f),
            (1, 7787, 1883.5f, 1218.25f),
            (1, 7787, 1886.93f, 1221.4f),
            (1, 8876, 1889.82f, 1222.51f),
            (1, 7788, 1893.07f, 1215.26f),
            (1, 7788, 1878.57f, 1214.16f),
            (1, 7788, 1883.74f, 1212.35f),
            (1, 8877, 1877, 1207.27f),
            (1, 8877, 1873.63f, 1204.65f),
            (1, 8876, 1877.4f, 1216.41f),
            (1, 8877, 1899.63f, 1202.52f),
            (2, 7789, 1902.83f, 1223.41f),
            (2, 8876, 1889.82f, 1222.51f),
            (2, 7787, 1883.5f, 1218.25f),
            (2, 7788, 1883.74f, 1212.35f),
            (2, 8877, 1877, 1207.27f),
            (2, 7787, 1890.08f, 1218.68f),
            (2, 7789, 1894.64f, 1206.29f),
            (2, 8876, 1877.4f, 1216.41f),
            (2, 7787, 1892.28f, 1225.49f),
            (2, 7788, 1893.07f, 1215.26f),
            (2, 8877, 1896.46f, 1205.62f),
            (2, 7789, 1874.45f, 1204.44f),
            (2, 7789, 1874.18f, 1221.24f),
            (2, 7787, 1879.02f, 1223.06f),
            (2, 8876, 1898.23f, 1217.97f),
            (2, 7787, 1882.07f, 1225.7f),
            (2, 8877, 1873.63f, 1204.65f),
            (2, 7787, 1886.97f, 1225.86f),
            (2, 7788, 1878.57f, 1214.16f),
            (2, 7787, 1894.72f, 1221.91f),
            (2, 7787, 1886.93f, 1221.4f),
            (2, 8876, 1883.76f, 1222.3f),
            (2, 7788, 1889.94f, 1212.21f),
            (2, 8877, 1899.63f, 1202.52f),
            (3, 7788, 1878.57f, 1214.16f),
            (3, 7787, 1894.72f, 1221.91f),
            (3, 7787, 1886.93f, 1221.4f),
            (3, 8876, 1883.76f, 1222.3f),
            (3, 7788, 1889.94f, 1212.21f),
            (3, 7275, 1889.23f, 1207.72f),
            (3, 7796, 1879.77f, 1207.96f),
        };
    }
}
