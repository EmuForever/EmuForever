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
using Game.Maps;
using Game.Scripting;
using System;
using static Game.Scripting.ScriptObject;
using static Scripts.Kalimdor.ZulFarrak.ZulFarrakData;

namespace Scripts.Kalimdor.ZulFarrak
{
    [Script]
    class boss_zum_rah : BossAI
    {

        public boss_zum_rah(Creature creature) : base(creature, BOSS_WITCH_DOCTOR_ZUM_RAH)
        {
            Initialize();
        }

        void Initialize()
        {
            _ward80 = false;
            _ward40 = false;
            _heal30 = false;
        }

        public override void Reset()
        {
            _Reset();
            me.SetFaction(35); // areatrigger sets faction to enemy
            Initialize();
        }

        public override void JustEngagedWith(Unit who)
        {
            _JustEngagedWith(who);
            Talk(SAY_SANCT_INVADE);
            _events.ScheduleEvent(EVENT_SHADOW_BOLT, TimeSpan.FromSeconds(1));
            _events.ScheduleEvent(EVENT_SHADOWBOLT_VOLLEY, TimeSpan.FromSeconds(10));
        }

        public override void KilledUnit(Unit victim)
        {
            Talk(SAY_KILL);
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            _events.Update(diff);

            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
            {
                switch (eventId)
                {
                    case EVENT_SHADOW_BOLT:
                        DoCastVictim(SPELL_SHADOW_BOLT);
                        _events.ScheduleEvent(EVENT_SHADOW_BOLT, TimeSpan.FromSeconds(4));
                        break;
                    case EVENT_WARD_OF_ZUM_RAH:
                        DoCast(me,SPELL_WARD_OF_ZUM_RAH);
                        break;
                    case EVENT_HEALING_WAVE:
                        DoCast(me,SPELL_HEALING_WAVE);
                        break;
                    case EVENT_SHADOWBOLT_VOLLEY:
                        if (SelectTarget(SelectTargetMethod.Random, 0) is Unit target)
                            DoCast(target, SPELL_SHADOWBOLT_VOLLEY);
                        _events.ScheduleEvent(EVENT_SHADOWBOLT_VOLLEY, TimeSpan.FromSeconds(9));
                        break;
                    default:
                        break;
                }
            }

            if (!_ward80 && HealthBelowPct(80))
            {
                _ward80 = true;
                Talk(SAY_WARD);
                _events.ScheduleEvent(EVENT_WARD_OF_ZUM_RAH, TimeSpan.FromSeconds(1));
            }

            if (!_ward40 && HealthBelowPct(40))
            {
                _ward40 = true;
                Talk(SAY_WARD);
                _events.ScheduleEvent(EVENT_WARD_OF_ZUM_RAH, TimeSpan.FromSeconds(1));
            }

            if (!_heal30 && HealthBelowPct(30))
            {
                _heal30 = true;
                _events.ScheduleEvent(EVENT_HEALING_WAVE, TimeSpan.FromSeconds(3));
            }
        }

        
            bool _ward80;
            bool _ward40;
            bool _heal30;

    
    }

    [Script]
    class npc_sergeant_bly : ScriptedAI
    {

        public npc_sergeant_bly(Creature creature) : base(creature)
        {
            Initialize();
            instance = creature.GetInstanceScript();
            postGossipStep = 0;
            Text_Timer = TimeSpan.FromMilliseconds(0);
        }

        void Initialize()
        {
            ShieldBash_Timer = TimeSpan.FromMilliseconds(5000);
            Revenge_Timer = TimeSpan.FromMilliseconds(8000);
        }

        InstanceScript  instance;

        int postGossipStep;
        TimeSpan Text_Timer;
        TimeSpan ShieldBash_Timer;
        TimeSpan Revenge_Timer;                                   //this is wrong, spell should never be used unless me.GetVictim() dodge, parry or block attack. Trinity support required.
        ObjectGuid PlayerGUID;

        public override void Reset()
        {
            Initialize();

            me.SetFaction(35);
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (postGossipStep>0 && postGossipStep<4)
            {
                if (Text_Timer<diff)
                {
                    switch (postGossipStep)
                    {
                        case 1:
                            //weegli doesn't fight - he goes & blows up the door
                            if (ObjectAccessor.GetCreature(me, instance.GetGuidData(ENTRY_WEEGLI)) is Creature pWeegli)
                                pWeegli.GetAI().DoAction(0);
                            Talk(SAY_1);
                            Text_Timer = TimeSpan.FromMilliseconds(5000);
                            break;
                        case 2:
                            Talk(SAY_2);
                            Text_Timer = TimeSpan.FromMilliseconds(5000);
                            break;
                        case 3:
                            me.SetFaction(14);
                            if (Global.ObjAccessor.GetPlayer(me, PlayerGUID) is Player target)
                                AttackStart(target);

                            switchFactionIfAlive(ENTRY_RAVEN);
                            switchFactionIfAlive(ENTRY_ORO);
                            switchFactionIfAlive(ENTRY_MURTA);
                            break;
                    }
                    postGossipStep++;
                }
                else Text_Timer -= diff;
            }

            if (!UpdateVictim())
                return;

            if (ShieldBash_Timer <= diff)
            {
                DoCastVictim(SPELL_SHIELD_BASH);
                ShieldBash_Timer = TimeSpan.FromMilliseconds(15000);
            }
            else
                ShieldBash_Timer -= diff;

            if (Revenge_Timer <= diff)
            {
                DoCastVictim(SPELL_REVENGE);
                Revenge_Timer = TimeSpan.FromMilliseconds(10000);
            }
            else
                Revenge_Timer -= diff;
        }

        public override void DoAction(int param)
        {
            postGossipStep=1;
            Text_Timer = TimeSpan.FromMilliseconds(0);
        }

        void switchFactionIfAlive(int entry)
        {
           if (ObjectAccessor.GetCreature(me, instance.GetGuidData(entry)) is Creature crew)
               if (crew.IsAlive())
                   crew.SetFaction(14);
        }

        public override bool OnGossipSelect(Player player, int menuId, int gossipListId)
        {
            int action = player.PlayerTalkClass.GetGossipOptionAction(gossipListId);
            ClearGossipMenuFor(player);
            if (action == eTradeskill.GossipActionInfoDef + 1)
            {
                CloseGossipMenuFor(player);
                PlayerGUID = player.GetGUID();
                DoAction(0);
            }
            return true;
        }

        public override bool OnGossipHello(Player player)
        {
            player.InitGossipMenu(GOSSIP_BLY_MID);
            if (instance.GetData(EVENT_PYRAMID) == PYRAMID_KILLED_ALL_TROLLS)
            {
                AddGossipItemFor(player, GOSSIP_BLY_MID, GOSSIP_BLY_OID, eTradeskill.GossipSenderMain, eTradeskill.GossipActionInfoDef + 1);
                SendGossipMenuFor(player, 1517, me.GetGUID());
            }
            else
                if (instance.GetData(EVENT_PYRAMID) == PYRAMID_NOT_STARTED)
                    SendGossipMenuFor(player, 1515, me.GetGUID());
                else
                    SendGossipMenuFor(player, 1516, me.GetGUID());
            return true;
        }
    
    }

    [Script]
    class npc_weegli_blastfuse : ScriptedAI
    {

        public npc_weegli_blastfuse(Creature creature) : base(creature)
        {
            instance = creature.GetInstanceScript();
            destroyingDoor = false;
            Bomb_Timer = TimeSpan.FromMilliseconds(10000);
            LandMine_Timer = TimeSpan.FromMilliseconds(30000);
        }

        TimeSpan Bomb_Timer;
        TimeSpan LandMine_Timer;
        bool destroyingDoor;
        InstanceScript  instance;

        public override void Reset()
        {
            
        }

        public override void AttackStart(Unit victim)
        {
            AttackStartCaster(victim, 10);//keep back & toss bombs/shoot
        }

        public override void JustDied(Unit killer)
        {
            
        }

        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim())
                return;

            if (Bomb_Timer < diff)
            {
                DoCastVictim(SPELL_BOMB);
                Bomb_Timer = TimeSpan.FromMilliseconds(10000);
            }
            else
                Bomb_Timer -= diff;

            if (me.IsAttackReady() && !me.IsWithinMeleeRange(me.GetVictim()))
            {
                DoCastVictim(SPELL_SHOOT);
                me.SetSheath(SheathState.Ranged);
            }
            else
            {
                me.SetSheath(SheathState.Melee);
            }
        }

        public override void MovementInform(MovementGeneratorType type, int id)
        {
            if (instance.GetData(EVENT_PYRAMID) == PYRAMID_CAGES_OPEN)
            {
                instance.SetData(EVENT_PYRAMID, PYRAMID_ARRIVED_AT_STAIR);
                Talk(SAY_WEEGLI_OHNO);
                me.SetHomePosition(1882.69f, 1272.28f, 41.87f, 0);
            }
            else
                if (destroyingDoor)
                {
                    instance.DoUseDoorOrButton(instance.GetGuidData(GO_END_DOOR));
                    /// @todo leave the area...
                    me.DespawnOrUnsummon();
                };
        }

        void DestroyDoor()
        {
            if (me.IsAlive())
            {
                me.SetFaction(35);
                me.GetMotionMaster().MovePoint(0, 1858.57f, 1146.35f, 14.745f);
                me.SetHomePosition(1858.57f, 1146.35f, 14.745f, 3.85f); // in case he gets interrupted
                Talk(SAY_WEEGLI_OK_I_GO);
                destroyingDoor = true;
            }
        }

        public override bool OnGossipSelect(Player player, int menuId, int gossipListId)
        {
            int action = player.PlayerTalkClass.GetGossipOptionAction(gossipListId);
            ClearGossipMenuFor(player);
            if (action == eTradeskill.GossipActionInfoDef + 1)
            {
                CloseGossipMenuFor(player);
                //here we make him run to door, set the charge and run away off to nowhere
                DestroyDoor();
            }
            return true;
        }

        public override bool OnGossipHello(Player player)
        {
            player.InitGossipMenu(GOSSIP_WEEGLI_MID);
            switch (instance.GetData(EVENT_PYRAMID))
            {
                case PYRAMID_KILLED_ALL_TROLLS:
                    AddGossipItemFor(player, GOSSIP_WEEGLI_MID, GOSSIP_WEEGLI_OID, eTradeskill.GossipSenderMain, eTradeskill.GossipActionInfoDef + 1);
                    SendGossipMenuFor(player, 1514, me.GetGUID());  //if event can proceed to end
                    break;
                case PYRAMID_NOT_STARTED:
                    SendGossipMenuFor(player, 1511, me.GetGUID());  //if event not started
                    break;
                default:
                    SendGossipMenuFor(player, 1513, me.GetGUID());  //if event are in progress
                    break;
            }
            return true;
        }
    
    }

    [Script]
    class go_troll_cage : GameObjectAI
    {

        public go_troll_cage(GameObject go) : base(go) { instance = go.GetInstanceScript(); }

        InstanceScript  instance;

        public override bool OnGossipHello(Player player)
        {
            instance.SetData(EVENT_PYRAMID, PYRAMID_CAGES_OPEN);
            //set bly & co to aggressive & start moving to top of stairs
            initBlyCrewMember(ENTRY_BLY, 1884.99f, 1263, 41.52f);
            initBlyCrewMember(ENTRY_RAVEN, 1882.5f, 1263, 41.52f);
            initBlyCrewMember(ENTRY_ORO, 1886.47f, 1270.68f, 41.68f);
            initBlyCrewMember(ENTRY_WEEGLI, 1890, 1263, 41.52f);
            initBlyCrewMember(ENTRY_MURTA, 1891.19f, 1272.03f, 41.60f);
            return false;
        }

    
        void initBlyCrewMember(int entry, float x, float y, float z)
        {
            if (ObjectAccessor.GetCreature(me, instance.GetGuidData(entry)) is Creature crew)
            {
                crew.SetReactState(ReactStates.Aggressive);
                crew.SetWalk(true);
                crew.SetHomePosition(x, y, z, 0);
                crew.GetMotionMaster().MovePoint(1, x, y, z);
                crew.SetFaction(250);
            }
        }
    
    }

    [Script]
    class go_shallow_grave : GameObjectAI
    {

        public go_shallow_grave(GameObject go) : base(go) { }

        public override bool OnGossipHello(Player player)
        {
            // randomly summon a zombie or dead hero the first time a grave is used
            if (me.GetUseCount() == 0)
            {
                int randomchance = Random.Shared.Next(0, 101);
                if (randomchance < CHANCE_ZOMBIE)
                    me.SummonCreature(NPC_ZOMBIE, me.GetPositionX(), me.GetPositionY(), me.GetPositionZ(), 0, TempSummonType.CorpseTimedDespawn, TimeSpan.FromSeconds(30));
                else
                    if ((randomchance - CHANCE_ZOMBIE) < CHANCE_DEAD_HERO)
                        me.SummonCreature(NPC_DEAD_HERO, me.GetPositionX(), me.GetPositionY(), me.GetPositionZ(), 0, TempSummonType.CorpseTimedDespawn, TimeSpan.FromSeconds(30));
            }
            me.AddUse();
            return false;
        }
    
    }
}
