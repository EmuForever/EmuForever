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

using Framework.Constants;
using Game.AI;
using Game.Entities;
using Game.Maps;
using Game.Scripting;
using Game.Spells;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Game.Networking.Packets;
using Game;
using static Scripts.Kalimdor.RuinsOfAhnQiraj.RuinsData;

namespace Scripts.Kalimdor.RuinsOfAhnQiraj
{
    [Script]
    class boss_ossirian : BossAI
    {
        const int SAY_SUPREME = 0;
        const int SAY_INTRO = 1;
        const int SAY_AGGRO = 2;
        const int SAY_SLAY = 3;
        const int SAY_DEATH = 4;
        const int SPELL_CURSE_OF_TONGUES = 25195;
        const int SPELL_CYCLONE = 25189;
        const int SPELL_SAND_STORM = 25160;
        const int SPELL_STOMP = 25188;
        const int SPELL_SUPREME = 25176;
        const int SPELL_SUMMON = 20477;
        const int SPELL_SUMMON_CRYSTAL = 25192;
        const int SPELL_WEAKNESS_FIRE = 25177;
        const int SPELL_WEAKNESS_FROST = 25178;
        const int SPELL_WEAKNESS_NATURE = 25180;
        const int SPELL_WEAKNESS_ARCANE = 25181;
        const int SPELL_WEAKNESS_SHADOW = 25183;
        const int ACTION_TRIGGER_WEAKNESS = 1;
        const int EVENT_SILENCE = 1;
        const int EVENT_CYCLONE = 2;
        const int EVENT_STOMP = 3;
        const int NUM_CRYSTALS = 12, NUM_TORNADOS = 5, NUM_WEAKNESS = 5;
        const float RoomRadius = 165;
        static readonly Position RoomCenter = new(-9343.041992f, 1923.278198f, 85.555984f, 0);
        static readonly int[] SpellWeakness = { SPELL_WEAKNESS_FIRE, SPELL_WEAKNESS_FROST, SPELL_WEAKNESS_NATURE, SPELL_WEAKNESS_ARCANE, SPELL_WEAKNESS_SHADOW };
        static readonly Position[] CrystalCoordinates = {
            new( -9188.4404296875f, 1940.2099609375f, 85.6390991210937f, 3.17650008201599f ),
            new( -9244.41015625f, 1808.97998046875f, 85.6390991210937f, 5.63741016387939f ),
            new( -9248.41015625f, 1974.82995605469f, 85.6390991210937f, 5.89920997619629f ),
            new( -9282.080078125f, 1887.33996582031f, 85.6390991210937f, 2.00712990760803f ),
            new( -9299.73046875f, 1748.44995117187f, 85.6390991210937f, 1.44861996173859f ),
            new( -9357.8603515625f, 1929.07995605469f, 85.6390991210937f, 1.06465005874634f ),
            new( -9367.169921875f, 1780.89001464844f, 85.6390991210937f, 1.90241003036499f ),
            new( -9383.2900390625f, 2012.68005371094f, 85.6511001586914f, 2.93214988708496f ),
            new( -9406.099609375f, 1862.38000488281f, 85.6390991210937f, 6.2308201789856f ),
            new( -9407.7197265625f, 1960.2099609375f, 85.6390991210937f, 1.11700999736786f ),
            new( -9432.400390625f, 1782.53002929687f, 85.6390991210937f, 5.86430978775024f ),
            new( -9506.1904296875f, 1865.56994628906f, 85.6390991210937f, 4.27606010437012f ) };

        public boss_ossirian(Creature creature) : base(creature, DATA_OSSIRIAN)
        {
            Initialize();
            SaidIntro = false;
        }

        void Initialize()
        {
            CrystalIterator = 0;
            TriggerGUID.Clear();
            CrystalGUID.Clear();
        }

        ObjectGuid TriggerGUID;
        ObjectGuid CrystalGUID;
        int CrystalIterator;
        bool SaidIntro;

        public override void Reset()
        {
            _Reset();
            Initialize();
        }

        public override void SpellHit(WorldObject caster, SpellInfo spellInfo)
        {
            for (int i = 0; i < NUM_WEAKNESS; ++i)
            {
                if (spellInfo.Id == SpellWeakness[i])
                {
                    me.RemoveAurasDueToSpell(SPELL_SUPREME);
                    // Despawn used crystal
                    if (caster.FindNearestGameObject(GO_OSSIRIAN_CRYSTAL, 5.0f) is GameObject crystal)
                        crystal.Delete();
                    if (caster.ToCreature() is Creature creatureCaster)
                        creatureCaster.DespawnOrUnsummon();
                    SpawnNextCrystal();
                }
            }
        }

        public override void DoAction(int action)
        {
            if (action == ACTION_TRIGGER_WEAKNESS)
                if (ObjectAccessor.GetCreature(me, TriggerGUID) is Creature Trigger)
                    if (!Trigger.HasUnitState(UnitState.Casting))
                        Trigger.CastSpell(Trigger, SpellWeakness[Random.Shared.Next(5)], false);
        }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            _events.Reset();
            _events.ScheduleEvent(EVENT_SILENCE, TimeSpan.FromSeconds(30));
            _events.ScheduleEvent(EVENT_CYCLONE, TimeSpan.FromSeconds(20));
            _events.ScheduleEvent(EVENT_STOMP, TimeSpan.FromSeconds(30));

            DoCastSelf(SPELL_SUPREME);
            Talk(SAY_AGGRO);

            Map map = me.GetMap();

            WeatherPkt weather = new(WeatherState.HeavySandstorm, 1.0f);
            map.SendToPlayers(weather);

            for (int i = 0; i < NUM_TORNADOS; ++i)
            {
                Position Point = me.GetRandomPoint(RoomCenter, RoomRadius);
                if (map.SummonCreature(NPC_SAND_VORTEX, Point) is Creature Tornado)
                    Tornado.CastSpell(Tornado, SPELL_SAND_STORM, true);
            }

            SpawnNextCrystal();
        }

        public override void KilledUnit(Unit victim)
        {
            Talk(SAY_SLAY);
        }

        public override void EnterEvadeMode(EvadeReason why)
        {
            Cleanup();
            summons.DespawnAll();
            base.EnterEvadeMode(why);
        }

        public override void JustDied(Unit killer)
        {
            Cleanup();
            _JustDied();
        }

        void Cleanup()
        {
            if (ObjectAccessor.GetGameObject(me, CrystalGUID) is GameObject Crystal)
                Crystal.Use(me);
        }

        void SpawnNextCrystal()
        {
            if (CrystalIterator == NUM_CRYSTALS)
                CrystalIterator = 0;

            if (me.SummonCreature(NPC_OSSIRIAN_TRIGGER, CrystalCoordinates[CrystalIterator]) is Creature Trigger)
            {
                TriggerGUID = Trigger.GetGUID();
                if (Trigger.SummonGameObject(GO_OSSIRIAN_CRYSTAL, CrystalCoordinates[CrystalIterator], Quaternion.CreateFromAxisAngle(Vector3.UnitZ, CrystalCoordinates[CrystalIterator].GetOrientation()), TimeSpan.FromDays(36500), GameObjectSummonType.TimedDespawn) is GameObject Crystal)
                {
                    CrystalGUID = Crystal.GetGUID();
                    ++CrystalIterator;
                }
            }
        }

        public override void MoveInLineOfSight(Unit who)

        {
            if (!SaidIntro)
            {
                Talk(SAY_INTRO);
                SaidIntro = true;
            }
            base.MoveInLineOfSight(who);
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            _events.Update(diff);

            // No kiting!
            if (me.GetDistance(me.GetVictim()) > 60.00f && me.GetDistance(me.GetVictim()) < 120.00f)
                DoCastVictim(SPELL_SUMMON);

            bool ApplySupreme = true;

            if (me.HasAura(SPELL_SUPREME))
                ApplySupreme = false;
            else
            {
                for (int i = 0; i < NUM_WEAKNESS; ++i)
                {
                    if (me.HasAura(SpellWeakness[i]))
                    {
                        ApplySupreme = false;
                        break;
                    }
                }
            }

            if (ApplySupreme)
            {
                DoCastSelf(SPELL_SUPREME);
                Talk(SAY_SUPREME);
            }

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_SILENCE:
                        DoCastAOE(SPELL_CURSE_OF_TONGUES);
                        _events.ScheduleEvent(EVENT_SILENCE, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
                        break;
                    case EVENT_CYCLONE:
                        DoCastVictim(SPELL_CYCLONE);
                        _events.ScheduleEvent(EVENT_CYCLONE, TimeSpan.FromSeconds(20));
                        break;
                    case EVENT_STOMP:
                        DoCastSelf(SPELL_STOMP);
                        _events.ScheduleEvent(EVENT_STOMP, TimeSpan.FromSeconds(30));
                        break;
                    default:
                        break;
                }
            }
        }

    }

    [Script]
    class go_ossirian_crystal : GameObjectAI
    {
        const int SAY_SUPREME = 0;
        const int SAY_INTRO = 1;
        const int SAY_AGGRO = 2;
        const int SAY_SLAY = 3;
        const int SAY_DEATH = 4;
        const int SPELL_CURSE_OF_TONGUES = 25195;
        const int SPELL_CYCLONE = 25189;
        const int SPELL_SAND_STORM = 25160;
        const int SPELL_STOMP = 25188;
        const int SPELL_SUPREME = 25176;
        const int SPELL_SUMMON = 20477;
        const int SPELL_SUMMON_CRYSTAL = 25192;
        const int SPELL_WEAKNESS_FIRE = 25177;
        const int SPELL_WEAKNESS_FROST = 25178;
        const int SPELL_WEAKNESS_NATURE = 25180;
        const int SPELL_WEAKNESS_ARCANE = 25181;
        const int SPELL_WEAKNESS_SHADOW = 25183;
        const int ACTION_TRIGGER_WEAKNESS = 1;
        const int EVENT_SILENCE = 1;
        const int EVENT_CYCLONE = 2;
        const int EVENT_STOMP = 3;
        const int NUM_CRYSTALS = 12, NUM_TORNADOS = 5, NUM_WEAKNESS = 5;
        const float RoomRadius = 165;
        static readonly Position RoomCenter = new(-9343.041992f, 1923.278198f, 85.555984f, 0);
        static readonly int[] SpellWeakness = { SPELL_WEAKNESS_FIRE, SPELL_WEAKNESS_FROST, SPELL_WEAKNESS_NATURE, SPELL_WEAKNESS_ARCANE, SPELL_WEAKNESS_SHADOW };
        static readonly Position[] CrystalCoordinates = {
            new( -9188.4404296875f, 1940.2099609375f, 85.6390991210937f, 3.17650008201599f ),
            new( -9244.41015625f, 1808.97998046875f, 85.6390991210937f, 5.63741016387939f ),
            new( -9248.41015625f, 1974.82995605469f, 85.6390991210937f, 5.89920997619629f ),
            new( -9282.080078125f, 1887.33996582031f, 85.6390991210937f, 2.00712990760803f ),
            new( -9299.73046875f, 1748.44995117187f, 85.6390991210937f, 1.44861996173859f ),
            new( -9357.8603515625f, 1929.07995605469f, 85.6390991210937f, 1.06465005874634f ),
            new( -9367.169921875f, 1780.89001464844f, 85.6390991210937f, 1.90241003036499f ),
            new( -9383.2900390625f, 2012.68005371094f, 85.6511001586914f, 2.93214988708496f ),
            new( -9406.099609375f, 1862.38000488281f, 85.6390991210937f, 6.2308201789856f ),
            new( -9407.7197265625f, 1960.2099609375f, 85.6390991210937f, 1.11700999736786f ),
            new( -9432.400390625f, 1782.53002929687f, 85.6390991210937f, 5.86430978775024f ),
            new( -9506.1904296875f, 1865.56994628906f, 85.6390991210937f, 4.27606010437012f ) };

        public go_ossirian_crystal(GameObject go) : base(go)
        { instance = go.GetInstanceScript(); }

        InstanceScript instance;

        public override bool OnGossipHello(Player player)
        {
            // Crystal animation on use
            me.SetLootState(LootState.Activated);
            Creature ossirian = player.FindNearestCreature(NPC_OSSIRIAN, 30.0f);
            if (ossirian == null || instance.GetBossState(DATA_OSSIRIAN) != EncounterState.InProgress)
                return false;

            ossirian.GetAI().DoAction(ACTION_TRIGGER_WEAKNESS);
            return true;
        }

    }
}
