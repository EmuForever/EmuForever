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
    class boss_houndmaster_loksey : BossAI
    {
        const int SAY_AGGRO = 0;
        const int SPELL_SUMMON_SCARLET_HOUND = 17164;
        const int SPELL_BLOODLUST = 6742;
        const int EVENT_BLOODLUST = 1;

        public boss_houndmaster_loksey(Creature creature) : base(creature, DATA_HOUNDMASTER_LOKSEY)
        { }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            Talk(SAY_AGGRO);
            DoCast(SPELL_SUMMON_SCARLET_HOUND);
            _events.ScheduleEvent(EVENT_BLOODLUST, TimeSpan.FromSeconds(20));
        }

        public override void ExecuteEvent(int eventId)
        {
            switch (eventId)
            {
                case EVENT_BLOODLUST:
                    if (me.HealthBelowPct(60))
                    {
                        DoCastSelf(SPELL_BLOODLUST);
                        _events.Repeat(TimeSpan.FromSeconds(60));
                    }
                    else
                        _events.Repeat(TimeSpan.FromSeconds(1));
                    break;
                default:
                    break;
            }
        }
    }
}
