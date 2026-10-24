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
    class boss_amnennar_the_coldbringer : BossAI
    {
        const int SAY_AGGRO = 0;
        const int SAY_SUMMON60 = 1;
        const int SAY_SUMMON30 = 2;
        const int SAY_HP = 3;
        const int SAY_KILL = 4;
        const int SPELL_AMNENNARSWRATH = 13009;
        const int SPELL_FROSTBOLT = 15530;
        const int SPELL_FROST_NOVA = 15531;
        const int SPELL_FROST_SPECTRES = 12642;
        const int EVENT_AMNENNARSWRATH = 1;
        const int EVENT_FROSTBOLT = 2;
        const int EVENT_FROST_NOVA = 3;
        const int DATA_AMNENNAR_THE_COLD_BRINGER = 3;

        public boss_amnennar_the_coldbringer(Creature creature) : base(creature, DATA_AMNENNAR_THE_COLD_BRINGER)
        {
            Initialize();
        }

        void Initialize()
        {
            hp60Spectrals = false;
            hp30Spectrals = false;
            hp50 = false;
        }

        public override void Reset()
        {
            _Reset();
            Initialize();
        }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            _events.ScheduleEvent(EVENT_AMNENNARSWRATH, TimeSpan.FromSeconds(8));
            _events.ScheduleEvent(EVENT_FROSTBOLT, TimeSpan.FromSeconds(1));
            _events.ScheduleEvent(EVENT_FROST_NOVA, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
            Talk(SAY_AGGRO);
        }

        public override void KilledUnit(Unit who)
        {
            if (who.GetTypeId() == TypeId.Player)
                Talk(SAY_KILL);
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
                    case EVENT_AMNENNARSWRATH:
                        DoCastVictim(SPELL_AMNENNARSWRATH);
                        _events.ScheduleEvent(EVENT_AMNENNARSWRATH, TimeSpan.FromSeconds(12));
                        break;
                    case EVENT_FROSTBOLT:
                        DoCastVictim(SPELL_FROSTBOLT);
                        _events.ScheduleEvent(EVENT_FROSTBOLT, TimeSpan.FromSeconds(8));
                        break;
                    case EVENT_FROST_NOVA:
                        DoCast(me, SPELL_FROST_NOVA);
                        _events.ScheduleEvent(EVENT_FROST_NOVA, TimeSpan.FromSeconds(15));
                        break;
                }

                if (me.HasUnitState(UnitState.Casting))
                    return;
            }

            if (!hp60Spectrals && HealthBelowPct(60))
            {
                Talk(SAY_SUMMON60);
                DoCastVictim(SPELL_FROST_SPECTRES);
                hp60Spectrals = true;
            }

            if (!hp50 && HealthBelowPct(50))
            {
                Talk(SAY_HP);
                hp50 = true;
            }

            if (!hp30Spectrals && HealthBelowPct(30))
            {
                Talk(SAY_SUMMON30);
                DoCastVictim(SPELL_FROST_SPECTRES);
                hp30Spectrals = true;
            }
        }

    
        bool hp60Spectrals;
        bool hp30Spectrals;
        bool hp50;
    
    }
}
