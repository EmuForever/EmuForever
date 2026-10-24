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
    class boss_moam : BossAI
    {
        const int EMOTE_AGGRO = 0;
        const int EMOTE_MANA_FULL = 1;
        const int SPELL_TRAMPLE = 15550;
        const int SPELL_DRAIN_MANA = 25671;
        const int SPELL_ARCANE_ERUPTION = 25672;
        const int SPELL_SUMMON_MANA_FIEND_1 = 25681;
        const int SPELL_SUMMON_MANA_FIEND_2 = 25682;
        const int SPELL_SUMMON_MANA_FIEND_3 = 25683;
        const int SPELL_ENERGIZE = 25685;
        const int EVENT_TRAMPLE = 1;
        const int EVENT_DRAIN_MANA = 2;
        const int EVENT_STONE_PHASE = 3;
        const int EVENT_STONE_PHASE_END = 4;
        const int EVENT_WIDE_SLASH = 5;
        const int ACTION_STONE_PHASE_START = 1;
        const int ACTION_STONE_PHASE_END = 2;

        public boss_moam(Creature creature) : base(creature, DATA_MOAM)
        {
            Initialize();
        }

        void Initialize()
        {
            _isStonePhase = false;
        }

        public override void Reset()
        {
            _Reset();
            me.SetPower(PowerType.Mana, 0);
            Initialize();
            _events.ScheduleEvent(EVENT_STONE_PHASE, TimeSpan.FromSeconds(90));
            //_events.ScheduleEvent(EVENT_WIDE_SLASH, TimeSpan.FromSeconds(11));
        }

        public override void DamageTaken(Unit attacker, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (!_isStonePhase && HealthBelowPct(45))
            {
                _isStonePhase = true;
                DoAction(ACTION_STONE_PHASE_START);
            }
        }

        public override void DoAction(int action)
        {
            switch (action)
            {
                case ACTION_STONE_PHASE_END:
                    {
                        me.RemoveAurasDueToSpell(SPELL_ENERGIZE);
                        _events.ScheduleEvent(EVENT_STONE_PHASE, TimeSpan.FromSeconds(90));
                        _isStonePhase = false;
                        break;
                    }
                case ACTION_STONE_PHASE_START:
                    {
                        DoCast(me, SPELL_SUMMON_MANA_FIEND_1);
                        DoCast(me, SPELL_SUMMON_MANA_FIEND_2);
                        DoCast(me, SPELL_SUMMON_MANA_FIEND_3);
                        DoCast(me, SPELL_ENERGIZE);
                        _events.ScheduleEvent(EVENT_STONE_PHASE_END, TimeSpan.FromSeconds(90));
                        break;
                    }
                default:
                    break;
            }
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            _events.Update(diff);

            if (me.GetPower(PowerType.Mana) == me.GetMaxPower(PowerType.Mana))
            {
                if (_isStonePhase)
                    DoAction(ACTION_STONE_PHASE_END);
                DoCastAOE(SPELL_ARCANE_ERUPTION);
                me.SetPower(PowerType.Mana, 0);
            }

            if (_isStonePhase)
            {
                if (_events.ExecuteEvent() == EVENT_STONE_PHASE_END)
                    DoAction(ACTION_STONE_PHASE_END);
                return;
            }

            // Messing up mana-drain channel
            //if (me.HasUnitState(UnitState.Casting))
            //    return;

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_STONE_PHASE:
                        DoAction(ACTION_STONE_PHASE_START);
                        break;
                    case EVENT_DRAIN_MANA:
                        {
                            List<Unit> targetList = me.GetThreatManager().GetModifiableThreatList().Select(reference => reference.GetVictim())
                                .Where(victim => victim.IsPlayer() && victim.GetPowerType() == PowerType.Mana)
                                .OrderBy(_ => Random.Shared.Next()).Take(5).ToList();
                            foreach (Unit target in targetList)
                                DoCast(target, SPELL_DRAIN_MANA);

                            _events.ScheduleEvent(EVENT_DRAIN_MANA, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
                            break;
                        }
                    default:
                        break;
                }
            }
        }

        bool _isStonePhase;

    }
}
