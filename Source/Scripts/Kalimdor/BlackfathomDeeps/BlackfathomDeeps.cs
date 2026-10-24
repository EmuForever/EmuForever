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
using Game.AI;
using Game.Entities;
using Game.Maps;
using Game.Scripting;
using Game.Spells;
using System;

namespace Scripts.Kalimdor.BlackfathomDeeps
{
    [Script]
    class instance_blackfathom_deeps : InstanceMapScript
    {
        public instance_blackfathom_deeps() : base(nameof(instance_blackfathom_deeps), 48) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new BlackfathomInstance(map);

        class BlackfathomInstance : InstanceScript
        {
            static readonly Position[] LorgusPositions =
            {
                new(-458.500610f, -38.343079f, -33.474445f), new(-469.423615f, -88.400513f, -39.265102f),
                new(-622.354980f, -10.350100f, -22.777000f), new(-759.640564f, 16.658913f, -29.159529f)
            };
            static readonly Position[] SpawnLocations =
            {
                new(-768.949f, -174.413f, -25.87f, 3.09f), new(-768.888f, -164.238f, -25.87f, 3.09f),
                new(-768.951f, -153.911f, -25.88f, 3.09f), new(-774.400f, -169.405f, -25.86f, 3.11f),
                new(-774.491f, -159.371f, -25.86f, 3.21f), new(-867.782f, -174.352f, -25.87f, 6.27f),
                new(-867.875f, -164.089f, -25.87f, 6.27f), new(-867.859f, -153.927f, -25.88f, 6.27f),
                new(-861.823f, -159.018f, -25.87f), new(-861.524f, -169.387f, -25.87f, 0.18f),
                new(-859.827f, -468.425f, -33.88f, 5.63f)
            };
            readonly ObjectGuid[] _shrines = new ObjectGuid[4];
            ObjectGuid _kelris, _gelihastShrine, _altar, _mainDoor;
            int _fires, _deaths;
            public BlackfathomInstance(InstanceMap map) : base(map)
            {
                SetHeaders("BFDv1");
                SetBossNumber(3);
            }
            public override void OnCreatureCreate(Creature creature)
            {
                switch (creature.GetEntry())
                {
                    case 4832: _kelris = creature.GetGUID(); break;
                    case 12902: creature.SetHomePosition(LorgusPositions[Random.Shared.Next(4)]); break;
                }
            }
            public override void OnGameObjectCreate(GameObject go)
            {
                switch (go.GetEntry())
                {
                    case 21118: _shrines[0] = go.GetGUID(); break;
                    case 21119: _shrines[1] = go.GetGUID(); break;
                    case 21120: _shrines[2] = go.GetGUID(); break;
                    case 21121: _shrines[3] = go.GetGUID(); break;
                    case 103015:
                        _gelihastShrine = go.GetGUID();
                        if (GetBossState(0) != EncounterState.Done) go.SetFlag(GameObjectFlags.NotSelectable);
                        break;
                    case 103016:
                        _altar = go.GetGUID();
                        if (GetBossState(2) != EncounterState.Done) go.SetFlag(GameObjectFlags.NotSelectable);
                        break;
                    case 21117:
                        if (GetBossState(2) == EncounterState.Done) HandleGameObject(ObjectGuid.Empty, true, go);
                        _mainDoor = go.GetGUID();
                        break;
                }
            }
            void SummonWave(int entry, params int[] positions)
            {
                GameObject shrine = instance.GetGameObject(_shrines[0]);
                if (shrine == null) return;
                foreach (int position in positions)
                    shrine.SummonCreature(entry, SpawnLocations[position], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(5));
            }
            public override void SetData(int type, int data)
            {
                switch (type)
                {
                    case 3:
                        _fires = data;
                        switch (_fires)
                        {
                            case 1: SummonWave(4825, 0, 2, 5, 7); break;
                            case 2: SummonWave(4977, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9); break;
                            case 3: SummonWave(4978, 1, 6); break;
                            case 4: SummonWave(4823, 0, 2, 5, 7); break;
                        }
                        break;
                    case 4:
                        _deaths = data;
                        if (_deaths == 18) HandleGameObject(_mainDoor, true);
                        break;
                }
            }
            public override bool SetBossState(int type, EncounterState state)
            {
                if (!base.SetBossState(type, state)) return false;
                if (type == 0 && state == EncounterState.Done)
                    instance.GetGameObject(_gelihastShrine)?.RemoveFlag(GameObjectFlags.NotSelectable);
                else if (type == 2 && state == EncounterState.Done)
                {
                    GameObject altar = instance.GetGameObject(_altar);
                    if (altar != null)
                    {
                        altar.RemoveFlag(GameObjectFlags.NotSelectable);
                        altar.SummonCreature(6729, SpawnLocations[10], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(5));
                    }
                }
                return true;
            }
            public override int GetData(int type) => type switch { 3 => _fires, 4 => _deaths, _ => 0 };
            public override ObjectGuid GetGuidData(int data) => data switch
            {
                >= 0 and <= 3 => _shrines[data], 4 => _kelris, 5 => _gelihastShrine, 7 => _mainDoor, _ => ObjectGuid.Empty
            };
        }
    }

    [Script]
    class boss_aku_mai : BossAI
    {
        bool _enraged;
        public boss_aku_mai(Creature creature) : base(creature, 2) { }
        public override void Reset() { _enraged = false; _Reset(); }
        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            _events.ScheduleEvent(1, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(9));
        }
        public override void DamageTaken(Unit attacker, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (!_enraged && me.HealthBelowPctDamaged(30, damage)) { DoCastSelf(3490); _enraged = true; }
        }
        public override void ExecuteEvent(int eventId)
        {
            if (eventId != 1) return;
            DoCastVictim(3815);
            _events.ScheduleEvent(1, TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(50));
        }
    }

    [Script]
    class boss_gelihast : BossAI
    {
        public boss_gelihast(Creature creature) : base(creature, 0) { }
        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            _events.ScheduleEvent(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        }
        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim()) return;
            _events.Update(diff);
            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
                if (eventId == 1)
                {
                    DoCastVictim(6533);
                    _events.ScheduleEvent(1, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(7));
                }
        }
    }

    [Script]
    class boss_kelris : BossAI
    {
        public boss_kelris(Creature creature) : base(creature, 1) { }
        public override void Reset() { _Reset(); DoCastSelf(8734); }
        public override void JustReachedHome() { base.JustReachedHome(); DoCastSelf(8734); }
        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            Talk(0);
            me.RemoveAurasDueToSpell(8734);
            _events.ScheduleEvent(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5));
            _events.ScheduleEvent(2, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(12));
        }
        public override void JustDied(Unit killer) { Talk(2); _JustDied(); }
        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim()) return;
            _events.Update(diff);
            if (me.HasUnitState(UnitState.Casting)) return;
            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case 1: DoCastVictim(15587); _events.ScheduleEvent(1, TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(9)); break;
                    case 2:
                        Unit target = SelectTarget(SelectTargetMethod.Random, 0, 100, true);
                        if (target != null) { Talk(1); DoCast(target, 8399); }
                        _events.ScheduleEvent(2, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));
                        break;
                }
                if (me.HasUnitState(UnitState.Casting)) return;
            }
        }
    }

    [Script]
    class go_blackfathom_altar : GameObjectAI
    {
        public go_blackfathom_altar(GameObject go) : base(go) { }
        public override bool OnGossipHello(Player player)
        {
            if (!player.HasAura(8733)) player.AddAura(8733, player);
            return true;
        }
    }

    [Script]
    class go_blackfathom_fire : GameObjectAI
    {
        readonly InstanceScript _instance;
        public go_blackfathom_fire(GameObject go) : base(go) => _instance = go.GetInstanceScript();
        public override bool OnGossipHello(Player player)
        {
            me.SetGoState(GameObjectState.Active);
            me.SetFlag(GameObjectFlags.NotSelectable);
            _instance.SetData(3, _instance.GetData(3) + 1);
            return true;
        }
    }

    [Script]
    class npc_blackfathom_deeps_event : ScriptedAI
    {
        readonly InstanceScript _instance;
        bool _flee;
        public npc_blackfathom_deeps_event(Creature creature) : base(creature) => _instance = me.GetInstanceScript();
        public override void JustEngagedWith(Unit who)
        {
            _flee = false;
            switch (me.GetEntry())
            {
                case 4825: _events.ScheduleEvent(1, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8)); break;
                case 4978:
                    _events.ScheduleEvent(2, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(12));
                    _events.ScheduleEvent(3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
                    break;
            }
        }
        public override void EnterEvadeMode(EvadeReason why = EvadeReason.Other) { _events.Reset(); base.EnterEvadeMode(why); }
        public override void IsSummonedBy(WorldObject summoner) => DoZoneInCombat();
        public override void JustDied(Unit killer)
        {
            if (me.IsSummon()) _instance.SetData(4, _instance.GetData(4) + 1);
        }
        public override void DamageTaken(Unit attacker, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (me.GetEntry() != 4977 && me.GetEntry() != 4823) return;
            if (!_flee && me.HealthBelowPctDamaged(15, damage)) { _flee = true; me.DoFleeToGetAssistance(); }
        }
        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim()) return;
            _events.Update(diff);
            if (me.HasUnitState(UnitState.Casting)) return;
            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
                switch (eventId)
                {
                    case 1: DoCastVictim(8391); _events.Repeat(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(14)); break;
                    case 2: DoCastAOE(865, false); _events.Repeat(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(30)); break;
                    case 3:
                        Unit target = SelectTarget(SelectTargetMethod.Random, 0);
                        if (target != null) DoCast(target, 8398);
                        _events.Repeat(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8));
                        break;
                }
        }
    }

    [Script]
    class npc_morridune : EscortAI
    {
        public npc_morridune(Creature creature) : base(creature) { }
        public override void Reset()
        {
            Talk(0);
            me.RemoveNpcFlag(NPCFlags1.Gossip);
            LoadPath(53834);
            Start(false);
        }
        public override void WaypointReached(int waypointId, int pathId)
        {
            if (waypointId != 4) return;
            SetEscortPaused(true);
            me.SetFacingTo(1.775791f);
            me.SetNpcFlag(NPCFlags1.Gossip);
            Talk(1);
        }
        public override bool OnGossipSelect(Player player, int menuId, int gossipListId)
        {
            DoCast(player, 9268);
            return false;
        }
    }
}

