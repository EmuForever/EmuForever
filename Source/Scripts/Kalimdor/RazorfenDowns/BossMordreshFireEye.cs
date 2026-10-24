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
    class boss_mordresh_fire_eye : BossAI
    {
        const int SAY_OOC_1 = 0;
        const int SAY_OOC_2 = 1;
        const int SAY_OOC_3 = 2;
        const int SAY_AGGRO = 3;
        const int SPELL_FIREBALL = 12466;
        const int SPELL_FIRE_NOVA = 12470;
        const int EVENT_OOC_1 = 1;
        const int EVENT_OOC_2 = 2;
        const int EVENT_OOC_3 = 3;
        const int EVENT_OOC_4 = 4;
        const int EVENT_FIREBALL = 5;
        const int EVENT_FIRE_NOVA = 6;
        const int DATA_MORDRESH_FIRE_EYE = 1;

        public boss_mordresh_fire_eye(Creature creature) : base(creature, DATA_MORDRESH_FIRE_EYE) { }

        public override void Reset()
        {
            _Reset();
            _events.ScheduleEvent(EVENT_OOC_1, TimeSpan.FromSeconds(10));
        }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            _events.Reset();
            Talk(SAY_AGGRO);
            _events.ScheduleEvent(EVENT_FIREBALL, TimeSpan.FromMilliseconds(100));
            _events.ScheduleEvent(EVENT_FIRE_NOVA, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(12));
        }

        public override void JustDied(Unit killer)
        {
            _JustDied();
        }

        public override void UpdateAI(TimeSpan diff)
        {
            _events.Update(diff);
            int eventId;

            if (!UpdateVictim())
            {
                while ((eventId = _events.ExecuteEvent()) != 0)
                {
                    switch (eventId)
                    {
                        case EVENT_OOC_1:
                            Talk(SAY_OOC_1);
                            _events.ScheduleEvent(EVENT_OOC_2, TimeSpan.FromSeconds(8));
                            break;
                        case EVENT_OOC_2:
                            Talk(SAY_OOC_2);
                            _events.ScheduleEvent(EVENT_OOC_3, TimeSpan.FromSeconds(3));
                            break;
                        case EVENT_OOC_3:
                            me.HandleEmoteCommand(Emote.OneshotExclamation);
                            _events.ScheduleEvent(EVENT_OOC_4, TimeSpan.FromSeconds(6));
                            break;
                        case EVENT_OOC_4:
                            Talk(SAY_OOC_3);
                            _events.ScheduleEvent(EVENT_OOC_1, TimeSpan.FromSeconds(14));
                            break;
                    }
                }
                return;
            }

            if (me.HasUnitState(UnitState.Casting))
                return;

            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_FIREBALL:
                        DoCastVictim(SPELL_FIREBALL);
                        _events.ScheduleEvent(EVENT_FIREBALL, TimeSpan.FromMilliseconds(2400), TimeSpan.FromMilliseconds(3800));
                        break;
                    case EVENT_FIRE_NOVA:
                        DoCast(me, SPELL_FIRE_NOVA);
                        _events.ScheduleEvent(EVENT_FIRE_NOVA, TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(16));
                        break;
                }

                if (me.HasUnitState(UnitState.Casting))
                    return;
            }
        }
    
    }
}
