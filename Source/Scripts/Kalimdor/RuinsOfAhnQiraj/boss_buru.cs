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
    class boss_buru : BossAI
    {
        const int EMOTE_TARGET = 0;
        const int SPELL_CREEPING_PLAGUE = 20512;
        const int SPELL_DISMEMBER = 96;
        const int SPELL_GATHERING_SPEED = 1834;
        const int SPELL_FULL_SPEED = 1557;
        const int SPELL_THORNS = 25640;
        const int SPELL_BURU_TRANSFORM = 24721;
        const int SPELL_SUMMON_HATCHLING = 1881;
        const int SPELL_EGG_EXPLOSION = 19593;
        const int SPELL_EXPLOSION_DAMAGE = 5255;
        const int SPELL_BURU_EGG_TRIGGER = 26646;
        const int EVENT_DISMEMBER = 1;
        const int EVENT_GATHERING_SPEED = 2;
        const int EVENT_FULL_SPEED = 3;
        const int EVENT_CREEPING_PLAGUE = 4;
        const int EVENT_RESPAWN_EGG = 5;
        const int PHASE_EGG = 0;
        const int PHASE_TRANSFORM = 1;
        const int ACTION_EXPLODE = 0;

        public boss_buru(Creature creature) : base(creature, DATA_BURU)
        {
            _phase = 0;
        }

        public override void EnterEvadeMode(EvadeReason why)
        {
            base.EnterEvadeMode(why);

            foreach (ObjectGuid eggGuid in Eggs)
                if (ObjectAccessor.GetCreature(me, eggGuid) is Creature egg)
                    egg.Respawn();

            Eggs.Clear();
        }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            Talk(EMOTE_TARGET, who);
            DoCast(me, SPELL_THORNS);

            _events.ScheduleEvent(EVENT_DISMEMBER, TimeSpan.FromSeconds(5));
            _events.ScheduleEvent(EVENT_GATHERING_SPEED, TimeSpan.FromSeconds(9));
            _events.ScheduleEvent(EVENT_FULL_SPEED, TimeSpan.FromMinutes(1));

            _phase = PHASE_EGG;
        }

        public override void DoAction(int action)
        {
            if (action == ACTION_EXPLODE)
                if (_phase == PHASE_EGG)
                    Unit.DealDamage(me, me, 45000);
        }

        public override void KilledUnit(Unit victim)
        {
            if (victim.GetTypeId() == TypeId.Player)
                ChaseNewVictim();
        }

        void ChaseNewVictim()
        {
            if (_phase != PHASE_EGG)
                return;

            me.RemoveAurasDueToSpell(SPELL_FULL_SPEED);
            me.RemoveAurasDueToSpell(SPELL_GATHERING_SPEED);
            _events.ScheduleEvent(EVENT_GATHERING_SPEED, TimeSpan.FromSeconds(9));
            _events.ScheduleEvent(EVENT_FULL_SPEED, TimeSpan.FromMinutes(1));

            if (SelectTarget(SelectTargetMethod.Random, 0, 0.0f, true) is Unit victim)
            {
                ResetThreatList();
                AttackStart(victim);
                Talk(EMOTE_TARGET, victim);
            }
        }

        public void ManageRespawn(ObjectGuid EggGUID)
        {
            ChaseNewVictim();
            Eggs.Add(EggGUID);
            _events.ScheduleEvent(EVENT_RESPAWN_EGG, TimeSpan.FromSeconds(100));
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            _events.Update(diff);

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_DISMEMBER:
                        DoCastVictim(SPELL_DISMEMBER);
                        _events.ScheduleEvent(EVENT_DISMEMBER, TimeSpan.FromSeconds(5));
                        break;
                    case EVENT_GATHERING_SPEED:
                        DoCast(me, SPELL_GATHERING_SPEED);
                        _events.ScheduleEvent(EVENT_GATHERING_SPEED, TimeSpan.FromSeconds(9));
                        break;
                    case EVENT_FULL_SPEED:
                        DoCast(me, SPELL_FULL_SPEED);
                        break;
                    case EVENT_CREEPING_PLAGUE:
                        DoCast(me, SPELL_CREEPING_PLAGUE);
                        _events.ScheduleEvent(EVENT_CREEPING_PLAGUE, TimeSpan.FromSeconds(6));
                        break;
                    case EVENT_RESPAWN_EGG:
                        if (Eggs.Count != 0 && ObjectAccessor.GetCreature(me, Eggs[0]) is Creature egg)
                        {
                            egg.Respawn();
                            Eggs.RemoveAt(0);
                        }
                        break;
                    default:
                        break;
                }
            }

            if (me.GetHealthPct() < 20.0f && _phase == PHASE_EGG)
            {
                DoCast(me, SPELL_BURU_TRANSFORM); // Enrage
                DoCast(me, SPELL_FULL_SPEED, true);
                me.RemoveAurasDueToSpell(SPELL_THORNS);
                _phase = PHASE_TRANSFORM;
            }
        }

        readonly List<ObjectGuid> Eggs = new();
        int _phase;

    }

    [Script]
    class npc_buru_egg : ScriptedAI
    {
        const int EMOTE_TARGET = 0;
        const int SPELL_CREEPING_PLAGUE = 20512;
        const int SPELL_DISMEMBER = 96;
        const int SPELL_GATHERING_SPEED = 1834;
        const int SPELL_FULL_SPEED = 1557;
        const int SPELL_THORNS = 25640;
        const int SPELL_BURU_TRANSFORM = 24721;
        const int SPELL_SUMMON_HATCHLING = 1881;
        const int SPELL_EGG_EXPLOSION = 19593;
        const int SPELL_EXPLOSION_DAMAGE = 5255;
        const int SPELL_BURU_EGG_TRIGGER = 26646;
        const int EVENT_DISMEMBER = 1;
        const int EVENT_GATHERING_SPEED = 2;
        const int EVENT_FULL_SPEED = 3;
        const int EVENT_CREEPING_PLAGUE = 4;
        const int EVENT_RESPAWN_EGG = 5;
        const int PHASE_EGG = 0;
        const int PHASE_TRANSFORM = 1;
        const int ACTION_EXPLODE = 0;

        public npc_buru_egg(Creature creature) : base(creature)
        {
            _instance = me.GetInstanceScript();
            SetCombatMovement(false);
        }

        public override void JustEngagedWith(Unit attacker)
        {
            if (ObjectAccessor.GetCreature(me, _instance.GetGuidData(DATA_BURU)) is Creature buru)
                if (!buru.IsInCombat())
                    buru.GetAI().AttackStart(attacker);
        }

        public override void JustSummoned(Creature who)
        {
            if (who.GetEntry() == NPC_HATCHLING)
                if (ObjectAccessor.GetCreature(me, _instance.GetGuidData(DATA_BURU)) is Creature buru)
                    if (buru.GetAI().SelectTarget(SelectTargetMethod.Random) is Unit target)
                        who.GetAI().AttackStart(target);
        }

        public override void JustDied(Unit killer)
        {
            DoCastAOE(SPELL_EGG_EXPLOSION, true);
            DoCastAOE(SPELL_SUMMON_HATCHLING, true);

            if (ObjectAccessor.GetCreature(me, _instance.GetGuidData(DATA_BURU)) is Creature buru)
                if (buru.GetAI() is boss_buru buruAI)
                    buruAI.ManageRespawn(me.GetGUID());
        }

        readonly InstanceScript _instance;

    }
}
