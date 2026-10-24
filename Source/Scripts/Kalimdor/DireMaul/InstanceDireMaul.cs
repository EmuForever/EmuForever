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
using System;
using System.Collections.Generic;
using System.Linq;

namespace Scripts.Kalimdor.DireMaul
{
    [Script]
    class instance_dire_maul : InstanceMapScript
    {
        public instance_dire_maul() : base(nameof(instance_dire_maul), 429) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new DireMaulInstance(map);
        class DireMaulInstance : InstanceScript
        {
            static readonly int[] CrystalEntries = { 177259, 177257, 177258, 179504, 179505 };
            readonly ObjectGuid[] _crystals = new ObjectGuid[5];
            // The reference's fixed four slots can overflow when extra mobs are present.
            readonly List<ObjectGuid>[] _crystalMobs = { new(), new(), new(), new(), new() };
            ObjectGuid _forcefield, _immolthar;
            public DireMaulInstance(InstanceMap map) : base(map)
            {
                SetHeaders("DM");
                SetBossNumber(24);
                LoadDungeonEncounterData(new DungeonEncounterData[]
                {
                    new(1,345), new(2,344), new(3,343), new(4,346), new(5,350), new(6,348), new(8,347),
                    new(9,349), new(10,361), new(11,362), new(12,363), new(13,364), new(14,365), new(15,366), new(23,367), new(16,368)
                });
            }
            public override void OnCreatureCreate(Creature creature)
            {
                if (creature.GetEntry() != 11496) return;
                _immolthar = creature.GetGUID();
                if (GetBossState(22) != EncounterState.Done) creature.SetUnitFlag(UnitFlags.NonAttackable);
            }
            public override void OnUnitDeath(Unit unit)
            {
                if (unit.GetEntry() == 14324) SetBossState(23, EncounterState.Done);
            }
            public override void OnGameObjectCreate(GameObject go)
            {
                base.OnGameObjectCreate(go);
                int crystal = Array.IndexOf(CrystalEntries, go.GetEntry());
                if (crystal >= 0) _crystals[crystal] = go.GetGUID();
                else if (go.GetEntry() == 179503)
                {
                    _forcefield = go.GetGUID();
                    if (GetBossState(22) != EncounterState.Done) _events.ScheduleEvent(1, TimeSpan.FromSeconds(1));
                }
            }
            public override ObjectGuid GetGuidData(int type)
            {
                int crystal = Array.IndexOf(CrystalEntries, type);
                return crystal >= 0 ? _crystals[crystal] : type switch { 179503 => _forcefield, 11496 => _immolthar, _ => ObjectGuid.Empty };
            }
            public override void Update(TimeSpan diff)
            {
                _events.Update(diff);
                int eventId;
                while ((eventId = _events.ExecuteEvent()) != 0)
                    switch (eventId)
                    {
                        case 1:
                            StoreCrystalCreatures();
                            _events.ScheduleEvent(2, TimeSpan.FromSeconds(3));
                            break;
                        case 2:
                            CheckCrystalCreatures();
                            if (GetBossState(22) != EncounterState.Done) _events.ScheduleEvent(2, TimeSpan.FromSeconds(1));
                            break;
                    }
            }
            void StoreCrystalCreatures()
            {
                for (int i = 0; i < _crystals.Length; ++i)
                {
                    _crystalMobs[i].Clear();
                    GameObject crystal = instance.GetGameObject(_crystals[i]);
                    if (crystal == null) continue;
                    foreach (int entry in new[] { 11480, 11483 })
                        foreach (Creature mob in crystal.GetCreatureListWithEntryInGrid(entry, 30)) _crystalMobs[i].Add(mob.GetGUID());
                }
            }
            void CheckCrystalCreatures()
            {
                for (int i = 0; i < _crystals.Length; ++i)
                {
                    GameObject crystal = instance.GetGameObject(_crystals[i]);
                    if (crystal == null || crystal.GetGoState() != GameObjectState.Ready) continue;
                    if (_crystalMobs[i].Any(guid => instance.GetCreature(guid)?.IsAlive() == true)) continue;
                    HandleGameObject(ObjectGuid.Empty, false, crystal);
                    SetBossState(17 + i, EncounterState.Done);
                    crystal.SetGoState(GameObjectState.Active);
                }
                for (int i = 0; i < 5; ++i)
                    if (GetBossState(17 + i) != EncounterState.Done) return;
                SetBossState(22, EncounterState.Done);
                instance.GetGameObject(_forcefield)?.SetGoState(GameObjectState.Active);
                instance.GetCreature(_immolthar)?.RemoveUnitFlag(UnitFlags.NonAttackable);
            }
        }
    }
}
