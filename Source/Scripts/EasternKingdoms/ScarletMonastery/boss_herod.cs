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
using static Scripts.EasternKingdoms.ScarletMonastery.ScarletMonasteryData;

namespace Scripts.EasternKingdoms.ScarletMonastery
{
    [Script]
    class boss_herod : BossAI
    {
        const int SAY_AGGRO = 0;
        const int SAY_WHIRLWIND = 1;
        const int SAY_ENRAGE = 2;
        const int SAY_KILL = 3;
        const int EMOTE_ENRAGE = 4;
        const int SPELL_RUSHINGCHARGE = 8260;
        const int SPELL_CLEAVE = 15496;
        const int SPELL_WHIRLWIND = 8989;
        const int SPELL_FRENZY = 8269;
        const int NPC_SCARLET_TRAINEE = 6575;
        const int NPC_SCARLET_MYRMIDON = 4295;
        const int EVENT_CLEAVE = 1;
        const int EVENT_WHIRLWIND = 2;
        static readonly Position ScarletTraineePos = new(1939.18f, -431.58f, 17.09f, 6.22f);

        public boss_herod(Creature creature) : base(creature, DATA_HEROD)
        {
            _enrage = false;
        }

        public override void Reset()
        {
            _enrage = false;
            _Reset();
        }

        public override void JustEngagedWith(Unit who)
        {
            Talk(SAY_AGGRO);
            DoCast(me, SPELL_RUSHINGCHARGE);
            base.JustEngagedWith(who);

            _events.ScheduleEvent(EVENT_CLEAVE, TimeSpan.FromSeconds(12));
            _events.ScheduleEvent(EVENT_WHIRLWIND, TimeSpan.FromMinutes(1));
        }

        public override void KilledUnit(Unit victim)
        {
            if (victim.GetTypeId() == TypeId.Player)
                Talk(SAY_KILL);
        }

        public override void JustDied(Unit killer)
        {
            _JustDied();

            for (int itr = 0; itr < 20; ++itr)
            {
                Position randomNearPosition = me.GetRandomPoint(ScarletTraineePos, 5.0f);
                randomNearPosition.SetOrientation(ScarletTraineePos.GetOrientation());
                me.SummonCreature(NPC_SCARLET_TRAINEE, randomNearPosition, TempSummonType.TimedOrDeadDespawn, TimeSpan.FromMinutes(10));
            }
        }

        public override void DamageTaken(Unit attacker, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (!_enrage && me.HealthBelowPctDamaged(30, damage))
            {
                Talk(EMOTE_ENRAGE);
                Talk(SAY_ENRAGE);
                DoCastSelf(SPELL_FRENZY);
                _enrage = true;
            }
        }

        public override void ExecuteEvent(int eventId)
        {
            switch (eventId)
            {
                case EVENT_CLEAVE:
                    DoCastVictim(SPELL_CLEAVE);
                    _events.Repeat(TimeSpan.FromSeconds(12));
                    break;
                case EVENT_WHIRLWIND:
                    Talk(SAY_WHIRLWIND);
                    DoCastVictim(SPELL_WHIRLWIND);
                    _events.Repeat(TimeSpan.FromSeconds(30));
                    break;
                default:
                    break;
            }
        }


        bool _enrage;
    }

    [Script]
    class npc_scarlet_trainee : EscortAI
    {
        const int SAY_AGGRO = 0;
        const int SAY_WHIRLWIND = 1;
        const int SAY_ENRAGE = 2;
        const int SAY_KILL = 3;
        const int EMOTE_ENRAGE = 4;
        const int SPELL_RUSHINGCHARGE = 8260;
        const int SPELL_CLEAVE = 15496;
        const int SPELL_WHIRLWIND = 8989;
        const int SPELL_FRENZY = 8269;
        const int NPC_SCARLET_TRAINEE = 6575;
        const int NPC_SCARLET_MYRMIDON = 4295;
        const int EVENT_CLEAVE = 1;
        const int EVENT_WHIRLWIND = 2;
        const int PATH_ESCORT_SCARLET_TRAINEE = 52602;

        public npc_scarlet_trainee(Creature creature) : base(creature)
        {
            _startTimer = TimeSpan.FromMilliseconds(Random.Shared.Next(1000, 6001));
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (_startTimer > TimeSpan.Zero)
            {
                if (_startTimer <= diff)
                {
                    LoadPath(PATH_ESCORT_SCARLET_TRAINEE);
                    Start(true);
                    _startTimer = TimeSpan.Zero;
                }
                else
                    _startTimer -= diff;
            }

            base.UpdateAI(diff);
        }


        TimeSpan _startTimer;
    }
}
