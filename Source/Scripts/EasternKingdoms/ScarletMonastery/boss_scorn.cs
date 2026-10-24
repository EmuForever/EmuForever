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
    class boss_scorn : BossAI
    {
        const int SPELL_LICHSLAP = 28873;
        const int SPELL_FROSTBOLT_VOLLEY = 8398;
        const int SPELL_MINDFLAY = 17313;
        const int SPELL_FROSTNOVA = 15531;
        const int EVENT_LICH_SLAP = 1;
        const int EVENT_FROSTBOLT_VOLLEY = 2;
        const int EVENT_MIND_FLAY = 3;
        const int EVENT_FROST_NOVA = 4;

        public boss_scorn(Creature creature) : base(creature, DATA_SCORN)
        { }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            _events.ScheduleEvent(EVENT_LICH_SLAP, TimeSpan.FromSeconds(45));
            _events.ScheduleEvent(EVENT_FROSTBOLT_VOLLEY, TimeSpan.FromSeconds(30));
            _events.ScheduleEvent(EVENT_MIND_FLAY, TimeSpan.FromSeconds(30));
            _events.ScheduleEvent(EVENT_FROST_NOVA, TimeSpan.FromSeconds(30));
        }

        public override void ExecuteEvent(int eventId)
        {
            switch (eventId)
            {
                case EVENT_LICH_SLAP:
                    DoCastVictim(SPELL_LICHSLAP);
                    _events.Repeat(TimeSpan.FromSeconds(45));
                    break;
                case EVENT_FROSTBOLT_VOLLEY:
                    DoCastVictim(SPELL_FROSTBOLT_VOLLEY);
                    _events.Repeat(TimeSpan.FromSeconds(20));
                    break;
                case EVENT_MIND_FLAY:
                    DoCastVictim(SPELL_MINDFLAY);
                    _events.Repeat(TimeSpan.FromSeconds(20));
                    break;
                case EVENT_FROST_NOVA:
                    DoCastVictim(SPELL_FROSTNOVA);
                    _events.Repeat(TimeSpan.FromSeconds(15));
                    break;
                default:
                    break;
            }
        }
    }
}
