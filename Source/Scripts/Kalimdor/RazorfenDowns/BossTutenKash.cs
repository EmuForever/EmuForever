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
using Game.Scripting;
using System;

namespace Scripts.Kalimdor.RazorfenDowns
{
    [Script]
    class boss_tuten_kash : BossAI
    {
        const int SPELL_THRASH = 8876;
        const int SPELL_WEB_SPRAY = 12252;
        const int SPELL_VIRULENT_POISON = 12254;
        const int SPELL_CURSE_OF_TUTENKASH = 12255;
        const int EVENT_WEB_SPRAY = 1;
        const int EVENT_CURSE_OF_TUTENKASH = 2;
        const int DATA_TUTEN_KASH = 0;

        public boss_tuten_kash(Creature creature) : base(creature, DATA_TUTEN_KASH) { }

        public override void Reset()
        {
            _Reset();
            if (!me.HasAura(SPELL_THRASH))
                DoCast(me, SPELL_THRASH);
            if (!me.HasAura(SPELL_VIRULENT_POISON))
                DoCast(me, SPELL_VIRULENT_POISON);
        }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            _events.ScheduleEvent(EVENT_WEB_SPRAY, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
            _events.ScheduleEvent(EVENT_CURSE_OF_TUTENKASH, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(14));
        }

        public override void JustDied(Unit killer)
        {
            _JustDied();
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            _events.Update(diff);
            int eventId;

            if (me.HasUnitState(UnitState.Casting))
                return;

            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_WEB_SPRAY:
                        if (SelectTarget(SelectTargetMethod.Random, 0, 100, false) is Unit target)
                        {
                            if (!target.HasAura(SPELL_WEB_SPRAY))
                                DoCast(target, SPELL_WEB_SPRAY);
                        }
                        _events.ScheduleEvent(EVENT_WEB_SPRAY, TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(8));
                        break;
                    case EVENT_CURSE_OF_TUTENKASH:
                        DoCast(me, SPELL_CURSE_OF_TUTENKASH);
                        _events.ScheduleEvent(EVENT_CURSE_OF_TUTENKASH, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(25));
                        break;
                }

                if (me.HasUnitState(UnitState.Casting))
                    return;
            }
        }
    
    }
}
