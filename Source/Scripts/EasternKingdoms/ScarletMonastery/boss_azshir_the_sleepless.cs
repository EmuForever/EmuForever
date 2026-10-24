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
    class boss_azshir_the_sleepless : BossAI
    {
        const int SPELL_CALL_OF_THE_GRAVE = 17831;
        const int SPELL_TERRIFY = 7399;
        const int SPELL_SOUL_SIPHON = 7290;
        const int EVENT_CALL_OF_GRAVE = 1;
        const int EVENT_TERRIFY = 2;
        const int EVENT_SOUL_SIPHON = 3;

        public boss_azshir_the_sleepless(Creature creature) : base(creature, DATA_AZSHIR)
        {
            _siphon = false;
        }

        public override void Reset()
        {
            _Reset();
            _siphon = false;
        }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            _events.ScheduleEvent(EVENT_CALL_OF_GRAVE, TimeSpan.FromSeconds(30));
            _events.ScheduleEvent(EVENT_TERRIFY, TimeSpan.FromSeconds(20));
        }

        public override void DamageTaken(Unit attacker, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (!_siphon && me.HealthBelowPctDamaged(50, damage))
            {
                DoCastVictim(SPELL_SOUL_SIPHON);
                _events.ScheduleEvent(EVENT_SOUL_SIPHON, TimeSpan.FromSeconds(20));
                _siphon = true;
            }
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            _events.Update(diff);

            if (me.HasUnitState(UnitState.Casting))
                return;

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_CALL_OF_GRAVE:
                        DoCastVictim(SPELL_CALL_OF_THE_GRAVE);
                        _events.Repeat(TimeSpan.FromSeconds(30));
                        break;
                    case EVENT_TERRIFY:
                        DoCastVictim(SPELL_TERRIFY);
                        _events.Repeat(TimeSpan.FromSeconds(20));
                        break;
                    case EVENT_SOUL_SIPHON:
                        DoCastVictim(SPELL_SOUL_SIPHON);
                        _events.Repeat(TimeSpan.FromSeconds(20));
                        break;
                    default:
                        break;
                }

                if (me.HasUnitState(UnitState.Casting))
                    return;
            }
        }


        bool _siphon;
    }
}
