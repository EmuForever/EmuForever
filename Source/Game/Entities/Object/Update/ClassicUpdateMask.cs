// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Game.Networking;
using System;

namespace Game.Entities
{
    /// <summary>
    /// Classic 1.60.1 (build 70425 and later) changes-mask numbering for UnitData, PlayerData and ActivePlayerData.
    /// The update writers use the retail (TrinityCore) bit numbers; the client reads a mask with Classic-only fields
    /// inserted, so the mask header is renumbered here. Ported from TrinityCore-forever UpdateFields.cpp.
    /// </summary>
    static class ClassicUpdateMask
    {
        public const uint NoBit = uint.MaxValue;

        /// <summary>Renumbers a retail mask to Classic bits, recomputes the Classic group bits and writes the blocks header.</summary>
        public static uint[] WriteHeader(WorldPacket data, UpdateMask changesMask, int retailBits, int classicBlockCount, Func<uint, uint> remap, (uint Group, uint Last)[] groups)
        {
            uint[] classicBlocks = new uint[classicBlockCount];
            for (uint bit = 0; bit < retailBits; ++bit)
            {
                if (!changesMask[(int)bit])
                    continue;
                uint cb = remap(bit);
                if (cb < classicBlockCount * 32)
                    classicBlocks[cb / 32] |= 1u << (int)(cb % 32);
            }

            foreach (var (group, last) in groups)
            {
                classicBlocks[group / 32] &= ~(1u << (int)(group % 32));
                for (uint cb = group + 1; cb <= last; ++cb)
                {
                    if ((classicBlocks[cb / 32] & (1u << (int)(cb % 32))) != 0)
                    {
                        classicBlocks[group / 32] |= 1u << (int)(group % 32);
                        break;
                    }
                }
            }

            uint classicBlocksMask = 0;
            for (int i = 0; i < classicBlockCount; ++i)
                if (classicBlocks[i] != 0)
                    classicBlocksMask |= 1u << i;

            data.WriteBits(classicBlocksMask, classicBlockCount);
            for (int i = 0; i < classicBlockCount; ++i)
                if (classicBlocks[i] != 0)
                    data.WriteBits(classicBlocks[i], 32);

            return classicBlocks;
        }

        // UnitData (client update reader rva 0x458D7F0). 70009 inserted bits 72, 86/87, 105, 110, 139, 141-143 and array
        // group bits; 70291 inserted the owner-only list as bit 140 before CurrentAreaID, so every 70009 bit from 140 is +1.
        public static uint UnitDataBit(uint bit)
        {
            uint classicBit = UnitDataBit70009(bit);
            if (classicBit >= 140)
                ++classicBit;
            // 70425 (update reader rva 0x37CE7A0): three owner-only fields after ManaCostMultiplier (bit 114) are bits 115-117,
            // so MaxHealthModifier..TaxiNodesID move from 115-131 to 118-134 (InteractSpellID 127, then past group bit 128) and
            // every later bit is three higher. e9e5593 put the three after TaxiNodesID instead.
            if (classicBit >= 115 && classicBit != 128)
            {
                bool belowGroup = classicBit < 128;
                classicBit += 3;
                if (belowGroup && classicBit >= 128)
                    ++classicBit;
            }
            return classicBit;
        }

        static uint UnitDataBit70009(uint bit)
        {
            if (bit == 230) return 141; // ClassicComboTarget
            if (bit == 231) return 86;  // ClassicTrainingPointsUsed
            if (bit == 232) return 87;  // ClassicTrainingPointsTotal
            if (bit == 70) return 71;  // VisFlags follows AnimTier in Classic
            if (bit == 71) return 70;
            if (bit <= 71) return bit;
            if (bit <= 84) return bit + 1;
            if (bit <= 92) return bit + 3;
            if (bit <= 95) return bit + 4;
            if (bit == 96) return 96;
            if (bit <= 101) return bit + 3;
            if (bit <= 105) return bit + 4;
            if (bit <= 122) return bit + 5;
            if (bit <= 127) return bit + 6;
            if (bit == 128) return 128;
            if (bit <= 133) return bit + 5;
            if (bit == 134) return 140;
            return bit + 9;                         // 135-138 -> 144-147, arrays (TC 139+) -> 148+
        }

        public static readonly (uint, uint)[] UnitDataGroups =
        [
            (0, 31), (32, 63), (64, 95), (96, 127), (128, 151), (152, 192), (193, 196), (197, 199), (200, 220), (221, 242)
        ];

        // PlayerData (client update reader rva 0xACB180): Classic inserted bit 19 (17-byte struct after GuildLevel),
        // 38 (surname), 50 (unknown) and 53 (17-byte struct after NpcAsPlayerInfo).
        public static uint PlayerDataBit(uint bit)
        {
            if (bit <= 18) return bit;
            if (bit <= 30) return bit + 1;
            if (bit == 31) return 33;           // TaxiMountAnimKitID moves past the group bit 32
            if (bit == 32) return 32;
            if (bit <= 36) return bit + 1;
            if (bit <= 47) return bit + 2;
            if (bit <= 49) return bit + 3;
            return bit + 4;
        }

        public static readonly (uint, uint)[] PlayerDataGroups =
        [
            (0, 31), (32, 53), (54, 56), (57, 232), (233, 252), (253, 259), (260, 292), (293, 309), (310, 329)
        ];

        // ActivePlayerData (client update reader rva 0xA91D90, 14 blocks): new fields 108, 111, 120, 151, 158; PetSpellPower removed;
        // TransmogMetadata has its own group (166/167); InvSlots has 145 entries (group 168), every array after it is shifted by 45.
        public static uint ActivePlayerDataBit(uint bit)
        {
            uint classicBit = ActivePlayerDataBit70291(bit);
            if (classicBit == NoBit || classicBit <= 120 || classicBit == 134 || classicBit == 166)
                return classicBit;
            if (classicBit == 133) return 135;
            if (classicBit == 165) return 167;
            if (classicBit == 167) return 168;
            return classicBit >= 168 ? classicBit + 2 : classicBit + 1;
        }

        static uint ActivePlayerDataBit70291(uint bit)
        {
            if (bit == 401) return 159; // NumStableSlots
            if (bit <= 107) return bit;
            if (bit <= 109) return bit + 1;
            if (bit <= 115) return bit + 2;
            if (bit == 116) return NoBit;       // PetSpellPower
            if (bit <= 118) return bit + 1;
            if (bit <= 131) return bit + 2;
            if (bit == 132) return 135;         // NumBankSlots
            if (bit == 133) return 136;         // NumCharacterBankTabs
            if (bit == 134) return 134;
            if (bit == 135) return 137;         // NumAccountBankTabs
            if (bit <= 148) return bit + 2;
            if (bit <= 154) return bit + 3;
            if (bit == 155) return 158;         // PetStable (separate from NumStableSlots)
            if (bit <= 161) return bit + 4;     // ..., ViewedOutfit (165)
            if (bit <= 271) return bit + 5;     // TransmogMetadata (167), InvSlots group (168) + InvSlots[0..107] (169..276)
            return bit + 42;                    // arrays after InvSlots (TC InvSlots has 108 of the client's 145)
        }

        public static readonly (uint, uint)[] ActivePlayerDataGroups =
        [
            (0, 31), (32, 69), (70, 101), (102, 133), (134, 165), (166, 169), (170, 315), (316, 318), (319, 347),
            (348, 354), (355, 379), (380, 412), (413, 417), (418, 420), (421, 426), (427, 444)
        ];
    }
}
