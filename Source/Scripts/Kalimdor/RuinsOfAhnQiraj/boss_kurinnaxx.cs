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
using Game;
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
    class boss_kurinnaxx : BossAI
    {
        const int SPELL_MORTALWOUND = 25646;
        const int SPELL_SANDTRAP = 25648;
        const int SPELL_ENRAGE = 26527;
        const int SPELL_SUMMON_PLAYER = 26446;
        const int SPELL_TRASH = 3391;
        const int SPELL_WIDE_SLASH = 25814;
        const int EVENT_MORTAL_WOUND = 1;
        const int EVENT_SANDTRAP = 2;
        const int EVENT_TRASH = 3;
        const int EVENT_WIDE_SLASH = 4;
        const int SAY_KURINAXX_DEATH = 5;

        public boss_kurinnaxx(Creature creature) : base(creature, DATA_KURINNAXX)
        {
            Initialize();
        }

        void Initialize()
        {
            _enraged = false;
        }

        public override void Reset()
        {
            _Reset();
            Initialize();
            _events.ScheduleEvent(EVENT_MORTAL_WOUND, TimeSpan.FromSeconds(8));
            _events.ScheduleEvent(EVENT_SANDTRAP, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
            _events.ScheduleEvent(EVENT_TRASH, TimeSpan.FromSeconds(1));
            _events.ScheduleEvent(EVENT_WIDE_SLASH, TimeSpan.FromSeconds(11));
        }

        public override void DamageTaken(Unit attacker, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (!_enraged && HealthBelowPct(30))
            {
                DoCast(me, SPELL_ENRAGE);
                _enraged = true;
            }
        }

        public override void JustDied(Unit killer)
        {
            _JustDied();
            if (ObjectAccessor.GetCreature(me, instance.GetGuidData(DATA_OSSIRIAN)) is Creature Ossirian)
                Global.CreatureTextMgr.SendChat(Ossirian, SAY_KURINAXX_DEATH, range: CreatureTextRange.Zone);
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
                    case EVENT_MORTAL_WOUND:
                        DoCastVictim(SPELL_MORTALWOUND);
                        _events.ScheduleEvent(EVENT_MORTAL_WOUND, TimeSpan.FromSeconds(8));
                        break;
                    case EVENT_SANDTRAP:
                        if (SelectTarget(SelectTargetMethod.Random, 0, 100, true) is Unit target)
                            target.CastSpell(target, SPELL_SANDTRAP, true);
                        else if (me.GetVictim() is Unit victim)
                            victim.CastSpell(victim, SPELL_SANDTRAP, true);
                        _events.ScheduleEvent(EVENT_SANDTRAP, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
                        break;
                    case EVENT_WIDE_SLASH:
                        DoCast(me, SPELL_WIDE_SLASH);
                        _events.ScheduleEvent(EVENT_WIDE_SLASH, TimeSpan.FromSeconds(11));
                        break;
                    case EVENT_TRASH:
                        DoCast(me, SPELL_TRASH);
                        _events.ScheduleEvent(EVENT_WIDE_SLASH, TimeSpan.FromSeconds(15));
                        break;
                    default:
                        break;
                }

                if (me.HasUnitState(UnitState.Casting))
                    return;
            }
        }

        bool _enraged;

    }
}
