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
    class boss_rajaxx : BossAI
    {
        const int SAY_ANDOROV_INTRO = 0;
        const int SAY_ANDOROV_ATTACK = 1;
        const int SAY_WAVE3 = 0;
        const int SAY_WAVE4 = 1;
        const int SAY_WAVE5 = 2;
        const int SAY_WAVE6 = 3;
        const int SAY_WAVE7 = 4;
        const int SAY_INTRO = 5;
        const int SAY_UNK1 = 6;
        const int SAY_UNK2 = 7;
        const int SAY_UNK3 = 8;
        const int SAY_DEATH = 9;
        const int SAY_CHANGEAGGRO = 10;
        const int SAY_KILLS_ANDOROV = 11;
        const int SAY_COMPLETE_QUEST = 12;
        const int SPELL_DISARM = 6713;
        const int SPELL_FRENZY = 8269;
        const int SPELL_THUNDERCRASH = 25599;
        const int EVENT_DISARM = 1;
        const int EVENT_THUNDERCRASH = 2;
        const int EVENT_CHANGE_AGGRO = 3;

        public boss_rajaxx(Creature creature) : base(creature, DATA_RAJAXX)
        {
            Initialize();
        }

        void Initialize()
        {
        }

        public override void Reset()
        {
            _Reset();
            Initialize();
            _events.ScheduleEvent(EVENT_DISARM, TimeSpan.FromSeconds(10));
            _events.ScheduleEvent(EVENT_THUNDERCRASH, TimeSpan.FromSeconds(12));
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
                    case EVENT_DISARM:
                        DoCastVictim(SPELL_DISARM);
                        _events.ScheduleEvent(EVENT_DISARM, TimeSpan.FromSeconds(22));
                        break;
                    case EVENT_THUNDERCRASH:
                        DoCast(me, SPELL_THUNDERCRASH);
                        _events.ScheduleEvent(EVENT_THUNDERCRASH, TimeSpan.FromSeconds(21));
                        break;
                    default:
                        break;
                }

                if (me.HasUnitState(UnitState.Casting))
                    return;
            }
        }


    }
}
