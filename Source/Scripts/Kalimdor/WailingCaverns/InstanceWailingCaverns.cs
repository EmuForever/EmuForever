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
using Game.Entities;
using Game.Maps;
using Game.Scripting;
using static Scripts.Kalimdor.WailingCaverns.WailingCavernsData;

namespace Scripts.Kalimdor.WailingCaverns
{
    [Script]
    class instance_wailing_caverns : InstanceMapScript
    {
        public instance_wailing_caverns() : base(nameof(instance_wailing_caverns), 43) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new WailingCavernsInstance(map);
        class WailingCavernsInstance : InstanceScript
        {
            bool _yelled;
            ObjectGuid _naralex;
            public WailingCavernsInstance(InstanceMap map) : base(map)
            {
                SetHeaders("WC");
                SetBossNumber(MAX_ENCOUNTER);
                LoadDungeonEncounterData(new DungeonEncounterData[]
                {
                    new(BOSS_LADY_ANACONDRA,585), new(BOSS_LORD_COBRAHN,586), new(BOSS_KRESH,587), new(BOSS_LORD_PYTHAS,588),
                    new(BOSS_SKUM,589), new(BOSS_LORD_SERPENTIS,590), new(BOSS_VERDAN_THE_EVERLIVING,591), new(BOSS_MUTANUS_THE_DEVOURER,592)
                });
            }
            public override void OnCreatureCreate(Creature creature)
            {
                if (creature.GetEntry() == DATA_NARALEX) _naralex = creature.GetGUID();
            }
            public override void OnUnitDeath(Unit unit)
            {
                switch (unit.GetEntry())
                {
                    case NPC_KRESH: SetBossState(BOSS_KRESH, EncounterState.Done); break;
                    case NPC_SKUM: SetBossState(BOSS_SKUM, EncounterState.Done); break;
                    case NPC_VERDAN_THE_EVERLIVING: SetBossState(BOSS_VERDAN_THE_EVERLIVING, EncounterState.Done); break;
                }
            }
            static int BossForData(int type) => type switch
            {
                TYPE_LORD_COBRAHN => BOSS_LORD_COBRAHN, TYPE_LORD_PYTHAS => BOSS_LORD_PYTHAS,
                TYPE_LADY_ANACONDRA => BOSS_LADY_ANACONDRA, TYPE_LORD_SERPENTIS => BOSS_LORD_SERPENTIS,
                TYPE_NARALEX_EVENT => 4, TYPE_NARALEX_PART1 => 5, TYPE_NARALEX_PART2 => 6,
                TYPE_NARALEX_PART3 => 7, TYPE_MUTANUS_THE_DEVOURER => BOSS_MUTANUS_THE_DEVOURER, _ => -1
            };
            public override void SetData(int type, int data)
            {
                if (type == TYPE_NARALEX_YELLED) _yelled = true;
                else if (BossForData(type) is int boss && boss >= 0) SetBossState(boss, (EncounterState)data);
            }
            public override int GetData(int type)
            {
                if (type == TYPE_NARALEX_YELLED) return _yelled ? 1 : 0;
                int boss = BossForData(type);
                return boss >= 0 ? (int)GetBossState(boss) : 0;
            }
            public override ObjectGuid GetGuidData(int data) => data == DATA_NARALEX ? _naralex : ObjectGuid.Empty;
        }
    }
}
