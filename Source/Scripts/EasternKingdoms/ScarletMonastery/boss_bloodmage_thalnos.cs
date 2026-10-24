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
    class boss_bloodmage_thalnos : BossAI
    {
        const int SAY_AGGRO = 0;
        const int SAY_HEALTH = 1;
        const int SAY_KILL = 2;
        const int SPELL_FLAMESHOCK = 8053;
        const int SPELL_SHADOWBOLT = 1106;
        const int SPELL_FLAMESPIKE = 8814;
        const int SPELL_FIRENOVA = 16079;
        const int EVENT_FLAME_SHOCK = 1;
        const int EVENT_SHADOW_BOLT = 2;
        const int EVENT_FLAME_SPIKE = 3;
        const int EVENT_FIRE_NOVA = 4;

        public boss_bloodmage_thalnos(Creature creature) : base(creature, DATA_BLOODMAGE_THALNOS)
        {
            _hpYell = false;
        }

        public override void Reset()
        {
            _hpYell = false;
            _Reset();
        }

        public override void JustEngagedWith(Unit who)
        {
            Talk(SAY_AGGRO);
            base.JustEngagedWith(who);
            _events.ScheduleEvent(EVENT_FLAME_SHOCK, TimeSpan.FromSeconds(10));
            _events.ScheduleEvent(EVENT_SHADOW_BOLT, TimeSpan.FromSeconds(2));
            _events.ScheduleEvent(EVENT_FLAME_SPIKE, TimeSpan.FromSeconds(8));
            _events.ScheduleEvent(EVENT_FIRE_NOVA, TimeSpan.FromSeconds(40));
        }

        public override void KilledUnit(Unit victim)
        {
            if (victim.GetTypeId() == TypeId.Player)
                Talk(SAY_KILL);
        }

        public override void DamageTaken(Unit attacker, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (!_hpYell && me.HealthBelowPctDamaged(35, damage))
            {
                Talk(SAY_HEALTH);
                _hpYell = true;
            }
        }

        public override void ExecuteEvent(int eventId)
        {
            switch (eventId)
            {
                case EVENT_FLAME_SHOCK:
                    DoCastVictim(SPELL_FLAMESHOCK);
                    _events.Repeat(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
                    break;
                case EVENT_SHADOW_BOLT:
                    DoCastVictim(SPELL_SHADOWBOLT);
                    _events.Repeat(TimeSpan.FromSeconds(2));
                    break;
                case EVENT_FLAME_SPIKE:
                    DoCastVictim(SPELL_FLAMESPIKE);
                    _events.Repeat(TimeSpan.FromSeconds(30));
                    break;
                case EVENT_FIRE_NOVA:
                    DoCastVictim(SPELL_FIRENOVA);
                    _events.Repeat(TimeSpan.FromSeconds(40));
                    break;
                default:
                    break;
            }
        }


        bool _hpYell;
    }
}
