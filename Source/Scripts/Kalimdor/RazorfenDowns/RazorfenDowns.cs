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
using Game.Maps;
using Game.Scripting;
using System;
using System.Numerics;

namespace Scripts.Kalimdor.RazorfenDowns
{
    [Script]
    class instance_razorfen_downs : InstanceMapScript
    {
        public instance_razorfen_downs() : base(nameof(instance_razorfen_downs), 129) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new RazorfenDownsInstance(map);
        class RazorfenDownsInstance : InstanceScript
        {
            static readonly Position[] SpawnPositions =
            {
                new(2487.339f,805.9111f,43.08361f,2.844887f), new(2485.405f,804.1145f,43.68511f,3.054326f),
                new(2488.431f,801.2809f,42.70374f,4.29351f), new(2489.914f,804.7949f,43.25175f,1.658063f),
                new(2541.246f,907.0941f,46.64201f,2.024582f), new(2544.701f,907.6331f,46.38007f,1.605703f),
                new(2541.49f,911.1756f,46.26493f,4.817109f), new(2544.693f,912.8887f,46.39912f,2.129302f),
                new(2524.036f,834.4852f,48.37031f,0.8028514f), new(2527.017f,829.9793f,48.06498f,0.6981317f),
                new(2542.818f,904.9359f,46.80911f,4.642576f), new(2543.287f,911.2448f,46.32785f,0.6806784f),
                new(2489.083f,806.5914f,43.21102f,3.682645f), new(2486.828f,802.8737f,43.19883f,2.9147f),
                new(2487.939f,804.2224f,43.10735f,1.692969f)
            };
            ObjectGuid _gong;
            int _wave, _fiendsKilled, _reaversKilled;
            public RazorfenDownsInstance(InstanceMap map) : base(map)
            {
                SetHeaders("RFD");
                // Upstream's idol event uses slot 6, beyond its declared five slots.
                // Allocate that slot so completion and saved fire removal can work.
                SetBossNumber(7);
            }
            public override void OnGameObjectCreate(GameObject go)
            {
                if (go.GetEntry() == 148917)
                {
                    _gong = go.GetGUID();
                    if (GetBossState(0) == EncounterState.Done) go.SetFlag(GameObjectFlags.NotSelectable);
                }
                else if ((go.GetEntry() == 151951 || go.GetEntry() == 151952 || go.GetEntry() == 151973) && GetBossState(6) == EncounterState.Done)
                    go.Delete();
            }
            public override void SetData(int type, int data)
            {
                if (type != 5) return;
                GameObject gong = instance.GetGameObject(_gong);
                if (data == (int)EncounterState.InProgress)
                {
                    gong?.SetFlag(GameObjectFlags.NotSelectable);
                    (int start, int end, int entry) = _wave switch
                    {
                        0 => (0, 10, 7349), 1 => (10, 14, 7351), 2 => (14, 15, 7355), _ => (0, 0, 0)
                    };
                    if (gong != null)
                        for (int i = start; i < end; ++i)
                        {
                            Creature creature = gong.SummonCreature(entry, SpawnPositions[i]);
                            creature?.GetMotionMaster().MovePoint(0, 2533.479f + Random.Shared.Next(-5, 6), 870.020f + Random.Shared.Next(-5, 6), 47.678f);
                        }
                    ++_wave;
                }
                else if (data == 7349 && ++_fiendsKilled == 10)
                {
                    _fiendsKilled = 0;
                    gong?.RemoveFlag(GameObjectFlags.NotSelectable);
                }
                else if (data == 7351 && ++_reaversKilled == 4)
                {
                    _reaversKilled = 0;
                    gong?.RemoveFlag(GameObjectFlags.NotSelectable);
                }
            }
        }
    }

    [Script]
    class npc_belnistrasz : ScriptedAI
    {
        static readonly Position[] SpawnerPositions =
        {
            new(2582.789f,954.3925f,52.48214f,3.787364f), new(2569.42f,956.3801f,52.27323f,5.427974f),
            new(2570.62f,942.3934f,53.7433f,0.715585f)
        };
        readonly InstanceScript _instance;
        bool _inProgress, _channeling;
        int _progress, _spawners;
        public npc_belnistrasz(Creature creature) : base(creature) => _instance = creature.GetInstanceScript();
        public override void Reset()
        {
            if (_inProgress) return;
            if (!me.HasAura(13326)) DoCastSelf(13326);
            _channeling = false;
            me.SetCanMelee(true);
            _progress = _spawners = 0;
            me.SetNpcFlag(NPCFlags1.QuestGiver);
        }
        public override void JustEngagedWith(Unit who)
        {
            if (_channeling) Talk(7, who);
            else
            {
                _events.ScheduleEvent(5, TimeSpan.FromSeconds(1));
                _events.ScheduleEvent(6, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12));
                if (Random.Shared.Next(101) > 40) Talk(6, who);
            }
        }
        public override void JustDied(Unit killer)
        {
            _instance.SetBossState(6, EncounterState.Done);
            me.DespawnOrUnsummon(TimeSpan.FromSeconds(5));
        }
        public override void OnQuestAccept(Player player, Quest quest)
        {
            if (quest.Id != 3525) return;
            _inProgress = true;
            Talk(0);
            me.RemoveNpcFlag(NPCFlags1.QuestGiver);
            me.SetFaction(250);
            me.GetMotionMaster().MovePath(6973680, false);
        }
        public override void MovementInform(MovementGeneratorType type, int id)
        {
            if (type != MovementGeneratorType.Waypoint || id != 17) return;
            _channeling = true;
            me.SetCanMelee(false);
            _events.ScheduleEvent(1, TimeSpan.FromSeconds(2));
        }
        public override void UpdateAI(TimeSpan diff)
        {
            if (!_inProgress) return;
            _events.Update(diff);
            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
                switch (eventId)
                {
                    case 1:
                        Talk(1);
                        DoCastSelf(12774);
                        _events.ScheduleEvent(2, TimeSpan.FromMilliseconds(100));
                        _events.ScheduleEvent(3, TimeSpan.FromSeconds(120));
                        break;
                    case 2:
                        Creature spawner = me.SummonCreature(8611, SpawnerPositions[Random.Shared.Next(3)], TempSummonType.TimedDespawn, TimeSpan.FromSeconds(4));
                        spawner?.GetAI().SetData(0, _spawners);
                        if (++_spawners < 8) _events.ScheduleEvent(2, TimeSpan.FromSeconds(35));
                        break;
                    case 3:
                        if (_progress < 3)
                        {
                            Talk(2 + _progress++);
                            _events.ScheduleEvent(3, TimeSpan.FromMinutes(1));
                        }
                        else
                        {
                            _events.CancelEvent(2);
                            me.InterruptSpell(CurrentSpellTypes.Channeled);
                            Talk(5);
                            _events.ScheduleEvent(4, TimeSpan.FromSeconds(3));
                        }
                        break;
                    case 4:
                        DoCastSelf(12816);
                        me.SummonGameObject(152097, new Position(2577.196f,947.0781f,53.16757f,2.356195f),
                            new Quaternion(0, 0, 0.9238796f, 0.3826832f), TimeSpan.FromHours(1), GameObjectSummonType.TimedDespawn);
                        foreach (Unit unit in me.GetPlayerListInGrid(50))
                        {
                            Player player = unit.ToPlayer();
                            if (player != null && player.GetQuestStatus(3525) == QuestStatus.Incomplete) player.GroupEventHappens(3525, me);
                        }
                        foreach (int entry in new[] { 151951, 151952, 151973 })
                            foreach (GameObject fire in me.GetGameObjectListWithEntryInGrid(entry, 50)) fire.Delete();
                        _instance.SetBossState(6, EncounterState.Done);
                        me.DespawnOrUnsummon();
                        break;
                    case 5:
                        if (me.HasUnitState(UnitState.Casting) || !UpdateVictim()) return;
                        DoCastVictim(9053);
                        _events.ScheduleEvent(5, TimeSpan.FromSeconds(8));
                        break;
                    case 6:
                        if (me.HasUnitState(UnitState.Casting) || !UpdateVictim()) return;
                        DoCastAOE(11831);
                        _events.ScheduleEvent(6, TimeSpan.FromSeconds(15));
                        break;
                }
        }
    }

    [Script]
    class npc_idol_room_spawner : ScriptedAI
    {
        public npc_idol_room_spawner(Creature creature) : base(creature) { }
        public override void Reset() { }
        void Summon(int entry, float angle, float distance) => me.SummonCreature(entry,
            new Position(me.GetPositionX() + MathF.Cos(angle) * distance, me.GetPositionY() + MathF.Sin(angle) * distance, me.GetPositionZ(), me.GetOrientation()));
        public override void SetData(int type, int data)
        {
            if (data < 7)
            {
                Summon(7333, 0, 0);
                if (data > 0 && me.GetOrientation() < 4) Summon(7333, 0, 0);
                Summon(7335, me.GetOrientation() - MathF.PI / 2, 2);
                Summon(7329, me.GetOrientation() + MathF.PI / 2, 2);
            }
            else if (data == 7) Summon(7356, 0, 0);
        }
    }

    [Script]
    class npc_tomb_creature : ScriptedAI
    {
        readonly InstanceScript _instance;
        public npc_tomb_creature(Creature creature) : base(creature) => _instance = creature.GetInstanceScript();
        public override void Reset()
        {
            if (me.GetEntry() == 7349 && !me.HasAura(3616)) DoCastSelf(3616);
            if (me.GetEntry() == 7351 && !me.HasAura(12254)) DoCastSelf(12254);
        }
        public override void JustDied(Unit killer) => _instance.SetData(5, me.GetEntry());
        public override void JustEngagedWith(Unit who) => _events.ScheduleEvent(7, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8));
        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim()) return;
            _events.Update(diff);
            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
                if (eventId == 7) { DoCastVictim(745); _events.ScheduleEvent(7, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(16)); }
        }
    }

    [Script]
    class go_gong : GameObjectAI
    {
        readonly InstanceScript _instance;
        public go_gong(GameObject go) : base(go) => _instance = go.GetInstanceScript();
        public override bool OnGossipHello(Player player)
        {
            me.SendCustomAnim(0);
            _instance.SetData(5, (int)EncounterState.InProgress);
            return true;
        }
    }
}
