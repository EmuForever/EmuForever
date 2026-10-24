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
    class boss_scarlet_commander_mograine : BossAI
    {
        const int SAY_MO_AGGRO = 0;
        const int SAY_MO_KILL = 1;
        const int SAY_MO_RESURRECTED = 2;
        const int SAY_WH_INTRO = 0;
        const int SAY_WH_KILL = 1;
        const int SAY_WH_RESURRECT = 2;
        const int SPELL_CRUSADER_STRIKE = 14518;
        const int SPELL_HAMMER_OF_JUSTICE = 5589;
        const int SPELL_LAY_ONHANDS = 9257;
        const int SPELL_RETRIBUTION_AURA = 8990;
        const int SPELL_DEEP_SLEEP = 9256;
        const int SPELL_SCARLET_RESURRECTION = 9232;
        const int SPELL_DOMINATE_MIND = 14515;
        const int SPELL_HOLY_SMITE = 9481;
        const int SPELL_HEAL = 12039;
        const int SPELL_POWER_WORD_SHIELD = 22187;
        const int EVENT_CRUSADER_STRIKE = 1;
        const int EVENT_HAMMER_OF_JUSTICE = 2;
        const int EVENT_HEAL = 1;
        const int EVENT_POWER_WORD_SHIELD = 2;
        const int EVENT_HOLY_SMITE = 3;
        const int POINT_WHITEMANE_MOVE_TO_MOGRAINE = 1;
        static readonly Position WhitemaneIntroMovePos = new(1163.113370f, 1398.856812f, 32.527786f, 0.0f);


        public boss_scarlet_commander_mograine(Creature creature) : base(creature, DATA_MOGRAINE_AND_WHITE_EVENT)
        {
            Initialize();
        }

        void Initialize()
        {
            _fakeDeath = false;
            _canDie = true;
        }

        public override void Reset()
        {
            Initialize();

            _Reset();
            _killYellTimer.Reset(TimeSpan.FromSeconds(0));

            DoCastSelf(SPELL_RETRIBUTION_AURA, true);
            me.RemoveUnitFlag(UnitFlags.NonAttackable);
            me.SetUninteractible(false);
            me.SetStandState(UnitStandStateType.Stand);
            me.SetReactState(ReactStates.Aggressive);

            instance.HandleGameObject(ObjectGuid.Empty, false, instance.GetGameObject(DATA_HIGH_INQUISITORS_DOOR));
        }

        public override void JustEngagedWith(Unit who)
        {
            base.JustEngagedWith(who);

            Talk(SAY_MO_AGGRO);

            // Call for help (from old script)
            me.CallForHelp(100.0f);

            // Just to be sure it's MOTION_SLOT_DEFAULT is static
            me.GetMotionMaster().MoveIdle();

            // Schedule _events
            _events.ScheduleEvent(EVENT_CRUSADER_STRIKE, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
            _events.ScheduleEvent(EVENT_HAMMER_OF_JUSTICE, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
        }

        public override void KilledUnit(Unit who)
        {
            if (who.GetTypeId() != TypeId.Player)
                return;

            if (_killYellTimer.Passed())
            {
                Talk(SAY_MO_KILL);
                _killYellTimer.Reset(TimeSpan.FromSeconds(5));
            }
        }

        public override void ExecuteEvent(int eventId)
        {
            switch (eventId)
            {
                case EVENT_CRUSADER_STRIKE:
                    DoCastVictim(SPELL_CRUSADER_STRIKE);
                    _events.Repeat(TimeSpan.FromSeconds(10));
                    break;
                case EVENT_HAMMER_OF_JUSTICE:
                    DoCastVictim(SPELL_HAMMER_OF_JUSTICE);
                    _events.Repeat(TimeSpan.FromSeconds(60));
                    break;
                default:
                    break;
            }
        }

        public override void DamageTaken(Unit who, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            if (damage >= me.GetHealth() && !_fakeDeath)
            {
                _fakeDeath = true;
                _canDie = false;

                // Open the door
                instance.HandleGameObject(ObjectGuid.Empty, true, instance.GetGameObject(DATA_HIGH_INQUISITORS_DOOR));

                // Tell whitemane to move
                if (instance.GetCreature(DATA_WHITEMANE) is Creature whitemane)
                {
                    whitemane.GetMotionMaster().MovePoint(0, WhitemaneIntroMovePos);
                    DoZoneInCombat(whitemane);
                }

                me.InterruptNonMeleeSpells(true);
                me.RemoveAllAuras();
                me.ClearAllReactives();
                me.SetUnitFlag(UnitFlags.NonAttackable);
                me.SetUninteractible(true);
                me.SetStandState(UnitStandStateType.Dead);
                me.SetReactState(ReactStates.Passive); // prevent Mograine from attacking while fake death

                // Stop moving
                me.GetMotionMaster().Clear();
            }

            if (!_canDie && damage >= me.GetHealth())
                damage = 0;
        }

        public override void SpellHit(WorldObject caster, SpellInfo spellInfo)
        {
            // Casted from Whitemane
            if (spellInfo.Id == SPELL_SCARLET_RESURRECTION)
            {
                _scheduler.Schedule(TimeSpan.FromSeconds(3), context =>
                {
                    // Say text
                    Talk(SAY_MO_RESURRECTED);

                    me.SetUninteractible(false);
                    me.SetStandState(UnitStandStateType.Stand);
                });

                _scheduler.Schedule(TimeSpan.FromSeconds(5), context =>
                {
                    // Schedule _events after ressurrect
                    _events.ScheduleEvent(EVENT_CRUSADER_STRIKE, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
                    _events.ScheduleEvent(EVENT_HAMMER_OF_JUSTICE, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));

                    // We can now die
                    me.RemoveUnitFlag(UnitFlags.NonAttackable);
                    me.SetReactState(ReactStates.Aggressive);
                    _canDie = true;
                    DoCastSelf(SPELL_RETRIBUTION_AURA, true);
                });
            }
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            _events.Update(diff);
            _scheduler.Update(diff);

            if (me.HasUnitState(UnitState.Casting))
                return;

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                ExecuteEvent(eventId);
                if (me.HasUnitState(UnitState.Casting))
                    return;
            }
        }


        readonly TimeTracker _killYellTimer = new();
        bool _fakeDeath;
        bool _canDie;
    }

    [Script]
    class boss_high_inquisitor_whitemane : ScriptedAI
    {
        const int SAY_MO_AGGRO = 0;
        const int SAY_MO_KILL = 1;
        const int SAY_MO_RESURRECTED = 2;
        const int SAY_WH_INTRO = 0;
        const int SAY_WH_KILL = 1;
        const int SAY_WH_RESURRECT = 2;
        const int SPELL_CRUSADER_STRIKE = 14518;
        const int SPELL_HAMMER_OF_JUSTICE = 5589;
        const int SPELL_LAY_ONHANDS = 9257;
        const int SPELL_RETRIBUTION_AURA = 8990;
        const int SPELL_DEEP_SLEEP = 9256;
        const int SPELL_SCARLET_RESURRECTION = 9232;
        const int SPELL_DOMINATE_MIND = 14515;
        const int SPELL_HOLY_SMITE = 9481;
        const int SPELL_HEAL = 12039;
        const int SPELL_POWER_WORD_SHIELD = 22187;
        const int EVENT_CRUSADER_STRIKE = 1;
        const int EVENT_HAMMER_OF_JUSTICE = 2;
        const int EVENT_HEAL = 1;
        const int EVENT_POWER_WORD_SHIELD = 2;
        const int EVENT_HOLY_SMITE = 3;
        const int POINT_WHITEMANE_MOVE_TO_MOGRAINE = 1;


        public boss_high_inquisitor_whitemane(Creature creature) : base(creature)
        {
            _instance = creature.GetInstanceScript();
            Initialize();
        }

        void Initialize()
        {
            _ressurectionInProgress = false;
            _canDie = true;
        }

        public override void Reset()
        {
            Initialize();

            _events.Reset();
            _scheduler.CancelAll();
            _killYellTimer.Reset(TimeSpan.FromSeconds(0));

            DoCastSelf(SPELL_RETRIBUTION_AURA);
            me.SetReactState(ReactStates.Aggressive);
        }

        public override void JustEngagedWith(Unit who)
        {
            Talk(SAY_WH_INTRO);

            // Just to be sure it's MOTION_SLOT_DEFAULT is static
            me.GetMotionMaster().MoveIdle();

            // Start _events after 5 seconds
            _scheduler.Schedule(TimeSpan.FromSeconds(5), context =>
            {
                _events.ScheduleEvent(EVENT_HEAL, TimeSpan.FromSeconds(10));
                _events.ScheduleEvent(EVENT_POWER_WORD_SHIELD, TimeSpan.FromSeconds(15));
                _events.ScheduleEvent(EVENT_HOLY_SMITE, TimeSpan.FromSeconds(6));
            });
        }

        public override void KilledUnit(Unit who)
        {
            if (who.GetTypeId() != TypeId.Player)
                return;

            if (_killYellTimer.Passed())
            {
                Talk(SAY_WH_KILL);
                _killYellTimer.Reset(TimeSpan.FromSeconds(5));
            }
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            _events.Update(diff);
            _scheduler.Update(diff);

            if (!_killYellTimer.Passed())
                _killYellTimer.Update(diff);

            if (me.HasUnitState(UnitState.Casting))
                return;

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_HEAL:
                        {
                            Creature target = null;
                            if (HealthBelowPct(75))
                                target = me;
                            else if (_instance.GetCreature(DATA_MOGRAINE) is Creature mograine)
                            {
                                if (mograine.IsAlive() && mograine.HealthBelowPct(75))
                                    target = mograine;
                            }

                            if (target != null)
                                DoCast(target, SPELL_HEAL);

                            _events.Repeat(TimeSpan.FromSeconds(13));
                            break;
                        }
                    case EVENT_POWER_WORD_SHIELD:
                        DoCastSelf(SPELL_POWER_WORD_SHIELD);
                        _events.Repeat(TimeSpan.FromSeconds(15));
                        break;
                    case EVENT_HOLY_SMITE:
                        DoCastVictim(SPELL_HOLY_SMITE);
                        _events.Repeat(TimeSpan.FromSeconds(6));
                        break;
                    default:
                        break;
                }

                if (me.HasUnitState(UnitState.Casting))
                    return;
            }
        }

        public override void DamageTaken(Unit who, ref int damage, DamageEffectType damageType, SpellInfo spellInfo = null)
        {
            // When Whitemane falls below 50% cast Deep sleep and schedule to ressurrect
            if (me.HealthBelowPctDamaged(50, damage) && !_ressurectionInProgress)
            {
                _ressurectionInProgress = true;
                _canDie = false;

                // Cancel all combat _events
                _events.CancelEvent(EVENT_HEAL);
                _events.CancelEvent(EVENT_POWER_WORD_SHIELD);
                _events.CancelEvent(EVENT_HOLY_SMITE);

                me.InterruptNonMeleeSpells(true);

                // Sleep all players
                DoCastAOE(SPELL_DEEP_SLEEP);

                if (_instance.GetCreature(DATA_MOGRAINE) is Creature mograine)
                {
                    me.SetReactState(ReactStates.Passive);
                    me.GetMotionMaster().Clear(MovementGeneratorPriority.Normal);

                    Position movePosition = new(mograine.GetPosition());

                    // Get a position within 2 yards of mograine, and facing him
                    me.MovePosition(movePosition, 2.0f, me.GetRelativeAngle(movePosition));
                    me.GetMotionMaster().MovePoint(POINT_WHITEMANE_MOVE_TO_MOGRAINE, movePosition);
                }
            }

            if (!_canDie && damage >= me.GetHealth())
                damage = 0;
        }

        public override void MovementInform(MovementGeneratorType type, int id)
        {
            if (type != MovementGeneratorType.Point || id != POINT_WHITEMANE_MOVE_TO_MOGRAINE)
                return;

            if (_instance.GetCreature(DATA_MOGRAINE) is Creature mograine)
                me.SetFacingToObject(mograine);

            // After 3 seconds cast scarlet ressurection
            _scheduler.Schedule(TimeSpan.FromSeconds(3), context =>
            {
                if (_instance.GetCreature(DATA_MOGRAINE) is Creature mograine)
                    DoCast(mograine, SPELL_SCARLET_RESURRECTION);
                else
                    MograineResurrected();
            });
        }

        public override void SpellHitTarget(WorldObject target, SpellInfo spellInfo)
        {
            if (target.GetEntry() == NPC_MOGRAINE && spellInfo.Id == SPELL_SCARLET_RESURRECTION)
                MograineResurrected();
        }


        void MograineResurrected()
        {
            Talk(SAY_WH_RESURRECT);

            // Schedule _events again
            _events.ScheduleEvent(EVENT_HEAL, TimeSpan.FromSeconds(10));
            _events.ScheduleEvent(EVENT_POWER_WORD_SHIELD, TimeSpan.FromSeconds(15));
            _events.ScheduleEvent(EVENT_HOLY_SMITE, TimeSpan.FromSeconds(6));

            _canDie = true;

            me.SetReactState(ReactStates.Aggressive);

            if (me.GetVictim() != null)
                me.GetMotionMaster().MoveChase(me.GetVictim());
        }

        readonly InstanceScript _instance;


        readonly TimeTracker _killYellTimer = new();
        bool _ressurectionInProgress;
        bool _canDie;
    }
}
