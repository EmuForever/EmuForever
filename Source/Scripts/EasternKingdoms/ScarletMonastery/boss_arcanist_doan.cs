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
    class boss_arcanist_doan : BossAI
    {
        const int SAY_AGGRO = 0;
        const int SAY_SPECIALAE = 1;
        const int SPELL_SILENCE = 8988;
        const int SPELL_ARCANE_EXPLOSION = 9433;
        const int SPELL_DETONATION = 9435;
        const int SPELL_ARCANE_BUBBLE = 9438;
        const int SPELL_POLYMORPH = 13323;
        const int EVENT_SILENCE = 1;
        const int EVENT_ARCANE_EXPLOSION = 2;
        const int EVENT_ARCANE_BUBBLE = 3;
        const int EVENT_POLYMORPH = 4;

        public boss_arcanist_doan(Creature creature) : base(creature, DATA_ARCANIST_DOAN)
        {
            _healthAbove50Pct = true;
        }

        public override void Reset()
        {
            _Reset();
            _healthAbove50Pct = true;
        }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            Talk(SAY_AGGRO);

            _events.ScheduleEvent(EVENT_SILENCE, TimeSpan.FromSeconds(15));
            _events.ScheduleEvent(EVENT_ARCANE_EXPLOSION, TimeSpan.FromSeconds(3));
            _events.ScheduleEvent(EVENT_POLYMORPH, TimeSpan.FromSeconds(30));
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
                    case EVENT_SILENCE:
                        DoCastVictim(SPELL_SILENCE);
                        _events.Repeat(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(20));
                        break;
                    case EVENT_ARCANE_EXPLOSION:
                        DoCastVictim(SPELL_ARCANE_EXPLOSION);
                        _events.Repeat(TimeSpan.FromSeconds(8));
                        break;
                    case EVENT_POLYMORPH:
                        if (SelectTarget(SelectTargetMethod.Random, 1, 30.0f, true) is Unit target1)
                            DoCast(target1, SPELL_POLYMORPH);
                        _events.Repeat(TimeSpan.FromSeconds(20));
                        break;
                    default:
                        break;
                }

                if (me.HasUnitState(UnitState.Casting))
                    return;
            }

            if (_healthAbove50Pct && HealthBelowPct(50))
            {
                _healthAbove50Pct = false;
                Talk(SAY_SPECIALAE);
                DoCastSelf(SPELL_ARCANE_BUBBLE);
                DoCastAOE(SPELL_DETONATION);
            }
        }


        bool _healthAbove50Pct;
    }
}
