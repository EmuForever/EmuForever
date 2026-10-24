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
using static Scripts.Kalimdor.RuinsOfAhnQiraj.RuinsData;

namespace Scripts.Kalimdor.RuinsOfAhnQiraj
{
    [Script]
    class boss_ayamiss : BossAI
    {
        const int SPELL_STINGER_SPRAY = 25749;
        const int SPELL_POISON_STINGER = 25748;
        const int SPELL_PARALYZE = 25725;
        const int SPELL_TRASH = 3391;
        const int SPELL_FRENZY = 8269;
        const int SPELL_LASH = 25852;
        const int SPELL_FEED = 25721;
        const int EVENT_STINGER_SPRAY = 1;
        const int EVENT_POISON_STINGER = 2;
        const int EVENT_SUMMON_SWARMER = 3;
        const int EVENT_SWARMER_ATTACK = 4;
        const int EVENT_PARALYZE = 5;
        const int EVENT_LASH = 6;
        const int EVENT_TRASH = 7;
        const int EMOTE_FRENZY = 0;
        const int PHASE_AIR = 0;
        const int PHASE_GROUND = 1;
        const int POINT_AIR = 0;
        const int POINT_GROUND = 1;
        const int POINT_PARALYZE = 2;
        static readonly Position AyamissAirPos = new(-9689.292f, 1547.912f, 48.02729f, 0);
        static readonly Position AltarPos = new(-9717.18f, 1517.72f, 27.4677f, 0);
        // Upstream flags these swarmer/larva positions as unverified.
        static readonly Position SwarmerPos = new(-9647.352f, 1578.062f, 55.32f, 0);
        static readonly Position[] LarvaPos = { new(-9674.4707f, 1528.4133f, 22.457f, 0), new(-9701.6005f, 1566.9993f, 24.118f, 0) };

        public boss_ayamiss(Creature creature) : base(creature, DATA_AYAMISS)
        {
            Initialize();
        }

        void Initialize()
        {
            _phase = PHASE_AIR;
            _enraged = false;
        }

        public override void Reset()
        {
            _Reset();
            Initialize();
            SetCombatMovement(false);
        }

        public override void JustSummoned(Creature who)
        {
            switch (who.GetEntry())
            {
                case NPC_SWARMER:
                    _swarmers.Add(who.GetGUID());
                    break;
                case NPC_LARVA:
                    who.GetMotionMaster().MovePoint(POINT_PARALYZE, AltarPos);
                    break;
                case NPC_HORNET:
                    if (SelectTarget(SelectTargetMethod.Random) is Unit target)
                        who.GetAI().AttackStart(target);
                    break;
            }
        }

        public override void MovementInform(MovementGeneratorType type, int id)
        {
            if (type == MovementGeneratorType.Point)
            {
                switch (id)
                {
                    case POINT_AIR:
                        me.AddUnitState(UnitState.Root);
                        break;
                    case POINT_GROUND:
                        me.ClearUnitState(UnitState.Root);
                        break;
                }
            }
        }

        public override void EnterEvadeMode(EvadeReason why)
        {
            me.ClearUnitState(UnitState.Root);
            base.EnterEvadeMode(why);
        }

        public override void JustEngagedWith(Unit attacker)
        {
            base.JustEngagedWith(attacker);

            _events.ScheduleEvent(EVENT_STINGER_SPRAY, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30));
            _events.ScheduleEvent(EVENT_POISON_STINGER, TimeSpan.FromSeconds(5));
            _events.ScheduleEvent(EVENT_SUMMON_SWARMER, TimeSpan.FromSeconds(5));
            _events.ScheduleEvent(EVENT_SWARMER_ATTACK, TimeSpan.FromMinutes(1));
            _events.ScheduleEvent(EVENT_PARALYZE, TimeSpan.FromSeconds(15));

            me.SetCanFly(true);
            me.SetDisableGravity(true);
            me.GetMotionMaster().MovePoint(POINT_AIR, AyamissAirPos);
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            _events.Update(diff);

            if (_phase == PHASE_AIR && me.GetHealthPct() < 70.0f)
            {
                _phase = PHASE_GROUND;
                SetCombatMovement(true);
                me.SetCanFly(false);
                if (me.GetVictim() != null)
                {
                    Position VictimPos = me.GetVictim().GetPosition();
                    me.GetMotionMaster().MovePoint(POINT_GROUND, VictimPos);
                }
                ResetThreatList();
                _events.ScheduleEvent(EVENT_LASH, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(8));
                _events.ScheduleEvent(EVENT_TRASH, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(6));
                _events.CancelEvent(EVENT_POISON_STINGER);
            }

            if (!_enraged && me.GetHealthPct() < 20.0f)
            {
                DoCast(me, SPELL_FRENZY);
                Talk(EMOTE_FRENZY);
                _enraged = true;
            }

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_STINGER_SPRAY:
                        DoCast(me, SPELL_STINGER_SPRAY);
                        _events.ScheduleEvent(EVENT_STINGER_SPRAY, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));
                        break;
                    case EVENT_POISON_STINGER:
                        DoCastVictim(SPELL_POISON_STINGER);
                        _events.ScheduleEvent(EVENT_POISON_STINGER, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3));
                        break;
                    case EVENT_PARALYZE:
                        if (SelectTarget(SelectTargetMethod.Random, 0, 0, true) is Unit target)
                        {
                            DoCast(target, SPELL_PARALYZE);
                            instance.SetGuidData(DATA_PARALYZED, target.GetGUID());
                            int Index = Random.Shared.Next(2);
                            me.SummonCreature(NPC_LARVA, LarvaPos[Index], TempSummonType.TimedOrCorpseDespawn, TimeSpan.FromSeconds(30));
                        }
                        _events.ScheduleEvent(EVENT_PARALYZE, TimeSpan.FromSeconds(15));
                        break;
                    case EVENT_SWARMER_ATTACK:
                        foreach (ObjectGuid guid in _swarmers)
                            if (ObjectAccessor.GetCreature(me, guid) is Creature swarmer)
                                if (SelectTarget(SelectTargetMethod.Random) is Unit swarmTarget)
                                    swarmer.GetAI().AttackStart(swarmTarget);

                        _swarmers.Clear();
                        _events.ScheduleEvent(EVENT_SWARMER_ATTACK, TimeSpan.FromMinutes(1));
                        break;
                    case EVENT_SUMMON_SWARMER:
                        {
                            Position Pos = me.GetRandomPoint(SwarmerPos, 80.0f);
                            me.SummonCreature(NPC_SWARMER, Pos);
                            _events.ScheduleEvent(EVENT_SUMMON_SWARMER, TimeSpan.FromSeconds(5));
                            break;
                        }
                    case EVENT_TRASH:
                        DoCastVictim(SPELL_TRASH);
                        _events.ScheduleEvent(EVENT_TRASH, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7));
                        break;
                    case EVENT_LASH:
                        DoCastVictim(SPELL_LASH);
                        _events.ScheduleEvent(EVENT_LASH, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(15));
                        break;
                }
            }
        }

        readonly List<ObjectGuid> _swarmers = new();
        int _phase;
        bool _enraged;

    }

    [Script]
    class npc_hive_zara_larva : ScriptedAI
    {
        const int SPELL_STINGER_SPRAY = 25749;
        const int SPELL_POISON_STINGER = 25748;
        const int SPELL_PARALYZE = 25725;
        const int SPELL_TRASH = 3391;
        const int SPELL_FRENZY = 8269;
        const int SPELL_LASH = 25852;
        const int SPELL_FEED = 25721;
        const int EVENT_STINGER_SPRAY = 1;
        const int EVENT_POISON_STINGER = 2;
        const int EVENT_SUMMON_SWARMER = 3;
        const int EVENT_SWARMER_ATTACK = 4;
        const int EVENT_PARALYZE = 5;
        const int EVENT_LASH = 6;
        const int EVENT_TRASH = 7;
        const int EMOTE_FRENZY = 0;
        const int PHASE_AIR = 0;
        const int PHASE_GROUND = 1;
        const int POINT_AIR = 0;
        const int POINT_GROUND = 1;
        const int POINT_PARALYZE = 2;
        static readonly Position AyamissAirPos = new(-9689.292f, 1547.912f, 48.02729f, 0);
        static readonly Position AltarPos = new(-9717.18f, 1517.72f, 27.4677f, 0);
        // Upstream flags these swarmer/larva positions as unverified.
        static readonly Position SwarmerPos = new(-9647.352f, 1578.062f, 55.32f, 0);
        static readonly Position[] LarvaPos = { new(-9674.4707f, 1528.4133f, 22.457f, 0), new(-9701.6005f, 1566.9993f, 24.118f, 0) };

        public npc_hive_zara_larva(Creature creature) : base(creature)
        {
            _instance = me.GetInstanceScript();
        }

        public override void MovementInform(MovementGeneratorType type, int id)
        {
            if (type == MovementGeneratorType.Point && id == POINT_PARALYZE)
                if (Global.ObjAccessor.GetUnit(me, _instance.GetGuidData(DATA_PARALYZED)) is Unit target)
                    if (target.GetTypeId() == TypeId.Player)
                        DoCast(target, SPELL_FEED); // Omnomnom
        }

        public override void MoveInLineOfSight(Unit who)
        {
            if (_instance.GetBossState(DATA_AYAMISS) == EncounterState.InProgress)
                return;

            base.MoveInLineOfSight(who);
        }

        public override void AttackStart(Unit victim)
        {
            if (_instance.GetBossState(DATA_AYAMISS) == EncounterState.InProgress)
                return;

            base.AttackStart(victim);
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (_instance.GetBossState(DATA_AYAMISS) == EncounterState.InProgress)
                return;

            base.UpdateAI(diff);
        }

        readonly InstanceScript _instance;

    }
}
