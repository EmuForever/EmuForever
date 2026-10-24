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
    class boss_glutton : BossAI
    {
        const int SAY_AGGRO = 0;
        const int SAY_SLAY = 1;
        const int SAY_HP50 = 2;
        const int SAY_HP15 = 3;
        const int SPELL_DISEASE_CLOUD = 12627;
        const int SPELL_FRENZY = 12795;
        const int DATA_GLUTTON = 2;

        public boss_glutton(Creature creature) : base(creature, DATA_GLUTTON)
        {
            hp50 = false;
            hp15 = false;
        }

        public override void Reset()
        {
            _Reset();
            hp50 = false;
            hp15 = false;
        }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);
            Talk(SAY_AGGRO);
        }

        public override void KilledUnit(Unit victim)
        {
            Talk(SAY_SLAY);
        }

        public override void JustDied(Unit killer)
        {
            _JustDied();
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            if (!hp50 && HealthBelowPct(50))
            {
                Talk(SAY_HP50);
                hp50 = true;
            }

            if (!hp15 && HealthBelowPct(15))
            {
                Talk(SAY_HP15);
                DoCast(me, SPELL_FRENZY);
                hp15 = true;
            }
        }

    
        bool hp50;
        bool hp15;
    
    }
}
