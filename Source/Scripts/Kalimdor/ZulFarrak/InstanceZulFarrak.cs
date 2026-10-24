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
using Game.DataStorage;
using Game.Entities;
using Game.Maps;
using Game.Scripting;
using System;
using System.Collections.Generic;
using System.Linq;
using static Scripts.Kalimdor.ZulFarrak.ZulFarrakData;

namespace Scripts.Kalimdor.ZulFarrak
{
    [Script]
    class instance_zulfarrak : InstanceMapScript
    {
        public instance_zulfarrak() : base(nameof(instance_zulfarrak), 209) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new ZulFarrakInstance(map);
        class ZulFarrakInstance : InstanceScript
        {
            readonly Dictionary<int, ObjectGuid> _guids = new();
            readonly Queue<ObjectGuid> _addsAtBase = new();
            readonly List<ObjectGuid> _movedAdds = new();
            int _gahzrilla, _phase, _groupSize;
            TimeSpan _majorTimer, _minorTimer;
            public ZulFarrakInstance(InstanceMap map) : base(map)
            {
                SetHeaders("ZF");
                SetBossNumber(MAX_ENCOUNTER);
                LoadDungeonEncounterData(new DungeonEncounterData[]
                {
                    new(0,593), new(1,594), new(2,595), new(3,596), new(4,597), new(5,598), new(6,599), new(7,600)
                });
            }
            public override void OnCreatureCreate(Creature creature)
            {
                switch (creature.GetEntry())
                {
                    case ENTRY_ZUM_RAH: _guids[creature.GetEntry()] = creature.GetGUID(); break;
                    case ENTRY_BLY: case ENTRY_RAVEN: case ENTRY_ORO: case ENTRY_WEEGLI: case ENTRY_MURTA:
                        _guids[creature.GetEntry()] = creature.GetGUID();
                        creature.SetReactState(ReactStates.Passive);
                        break;
                    case ENTRY_GAHZRILLA:
                        if (_gahzrilla >= (int)EncounterState.InProgress) creature.DespawnOrUnsummon();
                        else _gahzrilla = (int)EncounterState.InProgress;
                        break;
                }
            }
            public override void OnUnitDeath(Unit unit)
            {
                int boss = unit.GetEntry() switch
                {
                    ENTRY_VELRTHA => 0, ENTRY_GAHZRILLA => 1, ENTRY_ANTUSUL => 2, ENTRY_THEKA => 3,
                    ENTRY_NEKRUM => 5, ENTRY_SEZZZIZ => 6, ENTRY_SANDSCALP => 7, _ => -1
                };
                if (boss >= 0) SetBossState(boss, EncounterState.Done);
            }
            public override void OnGameObjectCreate(GameObject go)
            {
                if (go.GetEntry() == GO_END_DOOR) _guids[GO_END_DOOR] = go.GetGUID();
            }
            public override int GetData(int type) => type == EVENT_PYRAMID ? _phase : 0;
            public override ObjectGuid GetGuidData(int data) => _guids.GetValueOrDefault(data);
            public override void SetData(int type, int data) { if (type == EVENT_PYRAMID) _phase = data; }
            void SpawnWave(int wave)
            {
                foreach (var row in PyramidSpawns)
                {
                    if (row.Wave != wave) continue;
                    TempSummon summon = instance.SummonCreature(row.Entry, new Position(row.X, row.Y, 8.87f));
                    if (summon == null) continue;
                    summon.GetMotionMaster().MoveRandom(10);
                    _addsAtBase.Enqueue(summon.GetGUID());
                }
            }
            bool WaveIsDead() => !_addsAtBase.Concat(_movedAdds).Any(guid => instance.GetCreature(guid)?.IsAlive() == true);
            void SendAdds(int count)
            {
                while (count-- > 0 && _addsAtBase.TryDequeue(out ObjectGuid guid))
                {
                    Creature add = instance.GetCreature(guid);
                    if (add == null) continue;
                    add.GetMotionMaster().MovePath(PATH_ADDS, false);
                    _movedAdds.Add(guid);
                }
            }
            void MoveNPC(int entry, float x, float y, float z, float orientation)
            {
                Creature npc = instance.GetCreature(GetGuidData(entry));
                if (npc == null || !npc.IsAlive()) return;
                npc.SetWalk(true);
                npc.GetMotionMaster().MovePoint(1, x, y, z);
                npc.SetHomePosition(x, y, z, orientation);
            }
            public override void Update(TimeSpan diff)
            {
                switch (_phase)
                {
                    case PYRAMID_ARRIVED_AT_STAIR:
                        SpawnWave(1); _phase = PYRAMID_WAVE_1;
                        _majorTimer = TimeSpan.FromSeconds(120); _minorTimer = TimeSpan.Zero; _groupSize = 2;
                        break;
                    case PYRAMID_WAVE_1:
                        if (WaveIsDead()) { _phase = PYRAMID_PRE_WAVE_2; _majorTimer = TimeSpan.FromSeconds(10); }
                        else if (_minorTimer < diff) { SendAdds(_groupSize++); _minorTimer = TimeSpan.FromSeconds(10); }
                        else _minorTimer -= diff;
                        break;
                    case PYRAMID_PRE_WAVE_2:
                        if (_majorTimer < diff) { SpawnWave(2); _phase = PYRAMID_WAVE_2; _minorTimer = TimeSpan.Zero; _groupSize = 2; }
                        else _majorTimer -= diff;
                        break;
                    case PYRAMID_WAVE_2:
                        if (WaveIsDead()) { SpawnWave(3); _phase = PYRAMID_PRE_WAVE_3; _majorTimer = TimeSpan.FromSeconds(5); }
                        else if (_minorTimer < diff) { SendAdds(_groupSize++); _minorTimer = TimeSpan.FromSeconds(10); }
                        else _minorTimer -= diff;
                        break;
                    case PYRAMID_PRE_WAVE_3:
                        if (_majorTimer < diff)
                        {
                            MoveNPC(ENTRY_BLY,1887.92f,1228.179f,9.98f,4.78f); MoveNPC(ENTRY_MURTA,1891.57f,1228.68f,9.69f,4.78f);
                            MoveNPC(ENTRY_ORO,1897.23f,1228.34f,9.43f,4.78f); MoveNPC(ENTRY_RAVEN,1883.68f,1227.95f,9.543f,4.78f);
                            MoveNPC(ENTRY_WEEGLI,1878.02f,1227.65f,9.485f,4.78f); _phase = PYRAMID_WAVE_3;
                        }
                        else _majorTimer -= diff;
                        break;
                    case PYRAMID_WAVE_3:
                        if (WaveIsDead())
                        {
                            _phase = PYRAMID_KILLED_ALL_TROLLS;
                            MoveNPC(ENTRY_BLY,1883.82f,1200.83f,8.87f,1.32f); MoveNPC(ENTRY_MURTA,1891.83f,1201.45f,8.87f,1.32f);
                            MoveNPC(ENTRY_ORO,1894.50f,1204.40f,8.87f,1.32f); MoveNPC(ENTRY_RAVEN,1874.11f,1206.17f,8.87f,1.32f);
                            MoveNPC(ENTRY_WEEGLI,1877.52f,1199.63f,8.87f,1.32f);
                        }
                        break;
                }
            }
        }
    }

    [Script]
    class at_zumrah : AreaTriggerScript
    {
        public at_zumrah() : base(nameof(at_zumrah)) { }
        public override bool OnTrigger(Player player, AreaTriggerRecord areaTrigger)
        {
            Creature zumrah = player.FindNearestCreature(ZUMRAH_ID, 30);
            if (zumrah == null) return false;
            zumrah.SetFaction((int)FactionTemplates.TrollFrostmane);
            return true;
        }
    }
}
