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
    class boss_high_inquisitor_fairbanks : BossAI
    {
        const int SPELL_CURSEOFBLOOD = 8282;
        const int SPELL_DISPEL_MAGIC = 15090;
        const int SPELL_FEAR = 12096;
        const int SPELL_HEAL = 12039;
        const int SPELL_POWERWORDSHIELD = 11647;
        const int SPELL_SLEEP = 8399;
        const int EVENT_CURSE_BLOOD = 1;
        const int EVENT_DIPEL_MAGIC = 2;
        const int EVENT_FEAR = 3;
        const int EVENT_HEAL = 4;
        const int EVENT_SLEEP = 5;

        public boss_high_inquisitor_fairbanks(Creature creature) : base(creature, DATA_HIGH_INQUISITOR_FAIRBANKS)
        { }

        public override void Reset()
        {
            _Reset();
            _healTimer.Reset(TimeSpan.FromSeconds(0));
            _powerWordShield = false;
            me.SetStandState(UnitStandStateType.Dead);
        }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            _events.ScheduleEvent(EVENT_CURSE_BLOOD, TimeSpan.FromSeconds(10));
            _events.ScheduleEvent(EVENT_DIPEL_MAGIC, TimeSpan.FromSeconds(30));
            _events.ScheduleEvent(EVENT_FEAR, TimeSpan.FromSeconds(40));
            _events.ScheduleEvent(EVENT_SLEEP, TimeSpan.FromSeconds(25));
            me.SetStandState(UnitStandStateType.Stand);
        }

        public override void DamageTaken(Unit attacker, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (me.HealthBelowPctDamaged(25, damage))
            {
                if (!_powerWordShield)
                {
                    DoCastSelf(SPELL_POWERWORDSHIELD);
                    _powerWordShield = true;
                }

                if (!me.IsNonMeleeSpellCast(false) && _healTimer.Passed())
                {
                    _healTimer.Reset(TimeSpan.FromSeconds(30));
                    DoCastSelf(SPELL_HEAL);
                }
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
                ExecuteEvent(eventId);
                if (me.HasUnitState(UnitState.Casting))
                    return;
            }

            if (!_healTimer.Passed())
                _healTimer.Update(diff);
        }

        public override void ExecuteEvent(int eventId)
        {
            switch (eventId)
            {
                case EVENT_CURSE_BLOOD:
                    DoCastVictim(SPELL_CURSEOFBLOOD);
                    _events.Repeat(TimeSpan.FromSeconds(25));
                    break;
                case EVENT_DIPEL_MAGIC:
                    if (SelectTarget(SelectTargetMethod.Random, 0, unit => unit.IsPlayer() && me.GetDistance(unit) <= 30.0f &&
                        unit.GetDispellableAuraList(me, 1u << (int)DispelType.Magic).Count != 0) is Unit target1)
                        DoCast(target1, SPELL_DISPEL_MAGIC);
                    _events.Repeat(TimeSpan.FromSeconds(30));
                    break;
                case EVENT_FEAR:
                    if (SelectTarget(SelectTargetMethod.Random, 1, 20.0f, true) is Unit target2)
                        DoCast(target2, SPELL_FEAR);
                    _events.Repeat(TimeSpan.FromSeconds(40));
                    break;
                case EVENT_SLEEP:
                    if (SelectTarget(SelectTargetMethod.MaxThreat, 0, 30.0f, true, false) is Unit target3)
                        DoCast(target3, SPELL_SLEEP);
                    _events.Repeat(TimeSpan.FromSeconds(30));
                    break;
                default:
                    break;
            }
        }


        readonly TimeTracker _healTimer = new();
        bool _powerWordShield;
    }
}
