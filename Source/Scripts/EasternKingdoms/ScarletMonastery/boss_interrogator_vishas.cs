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
    class boss_interrogator_vishas : BossAI
    {
        const int SAY_AGGRO = 0;
        const int SAY_HEALTH1 = 1;
        const int SAY_HEALTH2 = 2;
        const int SAY_KILL = 3;
        const int SAY_TRIGGER_VORREL = 0;
        const int SPELL_SHADOW_WORD_PAIN = 2767;
        const int EVENT_SHADOW_WORD_PAIN = 1;

        public boss_interrogator_vishas(Creature creature) : base(creature, DATA_INTERROGATOR_VISHAS)
        {
            Initialize();
        }

        void Initialize()
        {
            _yellCount = 0;
        }

        public override void Reset()
        {
            Initialize();
            _Reset();
        }

        public override void JustEngagedWith(Unit who)
        {
            Talk(SAY_AGGRO);
            base.JustEngagedWith(who);
            _events.ScheduleEvent(EVENT_SHADOW_WORD_PAIN, TimeSpan.FromSeconds(5));
        }

        public override void KilledUnit(Unit victim)
        {
            if (victim.GetTypeId() == TypeId.Player)
                Talk(SAY_KILL);
        }

        public override void JustDied(Unit killer)
        {
            _JustDied();
            if (instance.GetCreature(DATA_VORREL) is Creature vorrel)
            {
                if (vorrel.GetAI() != null)
                    vorrel.GetAI().Talk(SAY_TRIGGER_VORREL);
            }
        }

        public override void DamageTaken(Unit attacker, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (me.HealthBelowPctDamaged(60, damage) && _yellCount < 1)
            {
                Talk(SAY_HEALTH1);
                ++_yellCount;
            }

            if (me.HealthBelowPctDamaged(30, damage) && _yellCount < 2)
            {
                Talk(SAY_HEALTH2);
                ++_yellCount;
            }
        }

        public override void ExecuteEvent(int eventId)
        {
            switch (eventId)
            {
                case EVENT_SHADOW_WORD_PAIN:
                    DoCastVictim(SPELL_SHADOW_WORD_PAIN);
                    _events.ScheduleEvent(EVENT_SHADOW_WORD_PAIN, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
                    break;
                default:
                    break;
            }
        }


        int _yellCount;
    }
}
