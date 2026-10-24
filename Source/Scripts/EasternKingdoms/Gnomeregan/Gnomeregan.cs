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
using System;
using System.Collections.Generic;
using System.Numerics;
using static Game.Scripting.ScriptObject;
using static Scripts.EasternKingdoms.Gnomeregan.GnomereganData;
namespace Scripts.EasternKingdoms.Gnomeregan
{
    [Script]
    class npc_blastmaster_emi_shortfuse : EscortAI
    {

        public npc_blastmaster_emi_shortfuse(Creature creature) : base(creature)
        {
            instance = creature.GetInstanceScript();
            creature.RestoreFaction();
            Initialize();
        }

        void Initialize()
        {
            uiTimer = TimeSpan.Zero;
            uiPhase = 0;
        }

        InstanceScript instance;

        int uiPhase;
        TimeSpan uiTimer;

        List<ObjectGuid> SummonList = new();
        List<ObjectGuid> GoSummonList = new();

        public override void Reset()
        {
            if (!HasEscortState(EscortState.Escorting))
            {
                Initialize();

                RestoreAll();

                SummonList.Clear();
                GoSummonList.Clear();
            }
        }

        public override bool OnGossipSelect(Player player, int menuId, int gossipListId)
        {
            if (gossipListId == 0)
            {
                LoadPath(PATH_ESCORT_BLASTMASTER_EMI);
                Start(true, player.GetGUID());

                me.SetFaction(player.GetFaction());
                SetData(1, 0);

                CloseGossipMenuFor(player);
            }
            return false;
        }

        void NextStep(int uiTimerStep, bool bNextStep = true, int uiPhaseStep = 0)
        {
            uiTimer = TimeSpan.FromMilliseconds(uiTimerStep);
            if (bNextStep)
                ++uiPhase;
            else
                uiPhase = uiPhaseStep;
        }

        void CaveDestruction(bool isRight)
        {
            if ((GoSummonList.Count == 0))
                return;

            foreach (ObjectGuid guid in GoSummonList)
            {
                if (ObjectAccessor.GetGameObject(me, guid) is GameObject go1)
                {
                    if (go1.SummonTrigger(go1.GetPositionX(), go1.GetPositionY(), go1.GetPositionZ(), 0, TimeSpan.FromMilliseconds(1)) is Creature trigger)
                    {
                        //visual effects are not working!
                        trigger.CastSpell(trigger, 11542, true);
                        trigger.CastSpell(trigger, 35470, true);
                    }
                    go1.RemoveFromWorld();
                    //go1.CastSpell(me, 12158); makes all die?!
                }
            }

            if (ObjectAccessor.GetGameObject(me, instance.GetGuidData(isRight ? DATA_GO_CAVE_IN_RIGHT : DATA_GO_CAVE_IN_LEFT)) is GameObject go2)
                instance.HandleGameObject(ObjectGuid.Empty, false, go2);
        }

        void SetInFace(bool isRight)
        {
            if (ObjectAccessor.GetGameObject(me, instance.GetGuidData(isRight ? DATA_GO_CAVE_IN_RIGHT : DATA_GO_CAVE_IN_LEFT)) is GameObject go3)
                me.SetFacingToObject(go3);
        }

        void RestoreAll()
        {
            if (ObjectAccessor.GetGameObject(me, instance.GetGuidData(DATA_GO_CAVE_IN_RIGHT)) is GameObject go4)
                instance.HandleGameObject(ObjectGuid.Empty, false, go4);

            if (ObjectAccessor.GetGameObject(me, instance.GetGuidData(DATA_GO_CAVE_IN_LEFT)) is GameObject go5)
                instance.HandleGameObject(ObjectGuid.Empty, false, go5);

            if (!(GoSummonList.Count == 0))
                foreach (ObjectGuid guid in GoSummonList)
                {
                    if (ObjectAccessor.GetGameObject(me, guid) is GameObject go6)
                        go6.RemoveFromWorld();
                }

            if (!(SummonList.Count == 0))
                foreach (ObjectGuid guid in SummonList)
                {
                    if (ObjectAccessor.GetCreature(me, guid) is Creature summon)
                    {
                        summon.DespawnOrUnsummon();
                    }
                }
        }

        void AggroAllPlayers(Creature temp)
        {

            foreach (Player player in me.GetMap().GetPlayers())
            {
                if (player != null)
                {
                    if (player.IsGameMaster())
                        continue;

                    if (player.IsAlive())
                        AddThreat(player, 0.0f, temp);
                }
            }
        }

        public override void WaypointReached(int waypointId, int pathId)
        {
            //just in case
            if (GetPlayerForEscort() != null)
                if (me.GetFaction() != GetPlayerForEscort().GetFaction())
                    me.SetFaction(GetPlayerForEscort().GetFaction());

            switch (waypointId)
            {
                case 3:
                    SetEscortPaused(true);
                    NextStep(2000, false, 3);
                    break;
                case 7:
                    SetEscortPaused(true);
                    NextStep(2000, false, 4);
                    break;
                case 9:
                    NextStep(1000, false, 8);
                    break;
                case 10:
                    NextStep(25000, false, 10);
                    break;
                case 11:
                    SetEscortPaused(true);
                    SetInFace(true);
                    NextStep(1000, false, 11);
                    break;
                case 12:
                    NextStep(25000, false, 18);
                    break;
                case 13:
                    Summon(6);
                    NextStep(25000, false, 19);
                    break;
                case 14:
                    SetInFace(false);
                    Talk(SAY_BLASTMASTER_17);
                    SetEscortPaused(true);
                    NextStep(5000, false, 20);
                    break;
            }
        }

        public override void SetData(int uiI, int uiValue)
        {
            switch (uiI)
            {
                case 1:
                    SetEscortPaused(true);
                    Talk(SAY_BLASTMASTER_0);
                    NextStep(2000, true);
                    break;
                case 2:
                    switch (uiValue)
                    {
                        case 1:
                            instance.SetBossState(DATA_BLASTMASTER_EVENT, EncounterState.InProgress);
                            break;
                        case 2:
                            instance.SetBossState(DATA_BLASTMASTER_EVENT, EncounterState.Done);
                            NextStep(5000, false, 22);
                            break;
                    }
                    break;
            }
        }

        void Summon(int uiCase)
        {
            switch (uiCase)
            {
                case 1:
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[0], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[1], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[2], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[3], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[4], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[5], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[6], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[7], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[8], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[9], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    break;
                case 2:
                    if (me.SummonGameObject(183410, -533.140f, -105.322f, -156.016f, 0.0f, Quaternion.Identity, TimeSpan.FromSeconds(1)) is GameObject go7)
                    {
                        GoSummonList.Add(go7.GetGUID());
                        go7.SetFlag(GameObjectFlags.NotSelectable); //We can't use it!
                    }
                    Summon(3);
                    break;
                case 3:
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[0], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[1], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[2], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[3], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    Talk(SAY_BLASTMASTER_7);
                    break;
                case 4:
                    if (me.SummonGameObject(183410, -542.199f, -96.854f, -155.790f, 0.0f, Quaternion.Identity, TimeSpan.FromSeconds(1)) is GameObject go8)
                    {
                        GoSummonList.Add(go8.GetGUID());
                        go8.SetFlag(GameObjectFlags.NotSelectable);
                    }
                    break;
                case 5:
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[10], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[11], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[12], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[13], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    me.SummonCreature(NPC_CAVERNDEEP_AMBUSHER, SpawnPosition[14], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    break;
                case 6:
                    if (me.SummonGameObject(183410, -507.820f, -103.333f, -151.353f, 0.0f, Quaternion.Identity, TimeSpan.FromSeconds(1)) is GameObject go9)
                    {
                        GoSummonList.Add(go9.GetGUID());
                        go9.SetFlag(GameObjectFlags.NotSelectable); //We can't use it!
                        Summon(5);
                    }
                    break;
                case 7:
                    if (me.SummonGameObject(183410, -511.829f, -86.249f, -151.431f, 0.0f, Quaternion.Identity, TimeSpan.FromSeconds(1)) is GameObject go10)
                    {
                        GoSummonList.Add(go10.GetGUID());
                        go10.SetFlag(GameObjectFlags.NotSelectable); //We can't use it!
                    }
                    break;
                case 8:
                    if (me.SummonCreature(NPC_GRUBBIS, SpawnPosition[15], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30)) is Creature grubbis)
                        grubbis.GetAI().Talk(SAY_GRUBBIS);
                    me.SummonCreature(NPC_CHOMPER, SpawnPosition[16], TempSummonType.CorpseTimedDespawn, TimeSpan.FromMinutes(30));
                    break;
                case 9:
                    me.SummonGameObject(GO_RED_ROCKET, SpawnPosition[17], Quaternion.CreateFromAxisAngle(Vector3.UnitZ, SpawnPosition[17].GetOrientation()), TimeSpan.FromHours(2));
                    me.SummonGameObject(GO_RED_ROCKET, SpawnPosition[18], Quaternion.CreateFromAxisAngle(Vector3.UnitZ, SpawnPosition[18].GetOrientation()), TimeSpan.FromHours(2));
                    me.SummonGameObject(GO_RED_ROCKET, SpawnPosition[19], Quaternion.CreateFromAxisAngle(Vector3.UnitZ, SpawnPosition[19].GetOrientation()), TimeSpan.FromHours(2));
                    break;
            }
        }

        public override void UpdateEscortAI(TimeSpan uiDiff)
        {
            if (uiPhase != 0)
            {
                if (uiTimer <= uiDiff)
                {
                    switch (uiPhase)
                    {
                        case 1:
                            Talk(SAY_BLASTMASTER_1);
                            NextStep(2000, true);
                            break;
                        case 2:
                            SetEscortPaused(false);
                            NextStep(0, false, 0);
                            break;
                        case 3:
                            Talk(SAY_BLASTMASTER_2);
                            SetEscortPaused(false);
                            NextStep(0, false, 0);
                            break;
                        case 4:
                            Talk(SAY_BLASTMASTER_3);
                            NextStep(3000, true);
                            break;
                        case 5:
                            Talk(SAY_BLASTMASTER_4);
                            NextStep(3000, true);
                            break;
                        case 6:
                            SetInFace(true);
                            Talk(SAY_BLASTMASTER_5);
                            Summon(1);
                            if (ObjectAccessor.GetGameObject(me, instance.GetGuidData(DATA_GO_CAVE_IN_RIGHT)) is GameObject go11)
                                instance.HandleGameObject(ObjectGuid.Empty, true, go11);
                            NextStep(3000, true);
                            break;
                        case 7:
                            Talk(SAY_BLASTMASTER_6);
                            SetEscortPaused(false);
                            NextStep(0, false, 0);
                            break;
                        case 8:
                            me.HandleEmoteCommand(Emote.StateUseStanding);
                            NextStep(25000, true);
                            break;
                        case 9:
                            Summon(2);
                            NextStep(0, false);
                            break;
                        case 10:
                            Summon(4);
                            Talk(SAY_BLASTMASTER_8);
                            NextStep(0, false);
                            break;
                        case 11:
                            Talk(SAY_BLASTMASTER_9);
                            NextStep(5000, true);
                            break;
                        case 12:
                            Talk(SAY_BLASTMASTER_10);
                            NextStep(5000, true);
                            break;
                        case 13:
                            Talk(SAY_BLASTMASTER_11);
                            CaveDestruction(true);
                            NextStep(8000, true);
                            break;
                        case 14:
                            Talk(SAY_BLASTMASTER_12);
                            NextStep(8500, true);
                            break;
                        case 15:
                            Talk(SAY_BLASTMASTER_13);
                            NextStep(2000, true);
                            break;
                        case 16:
                            Talk(SAY_BLASTMASTER_14);
                            SetInFace(false);
                            if (ObjectAccessor.GetGameObject(me, instance.GetGuidData(DATA_GO_CAVE_IN_LEFT)) is GameObject go12)
                                instance.HandleGameObject(ObjectGuid.Empty, true, go12);
                            NextStep(2000, true);
                            break;
                        case 17:
                            SetEscortPaused(false);
                            Talk(SAY_BLASTMASTER_15);
                            Summon(5);
                            NextStep(0, false);
                            break;
                        case 18:
                            Summon(6);
                            NextStep(0, false);
                            break;
                        case 19:
                            SetInFace(false);
                            Summon(7);
                            Talk(SAY_BLASTMASTER_16);
                            NextStep(0, false);
                            break;
                        case 20:
                            Talk(SAY_BLASTMASTER_18);
                            NextStep(2000, true);
                            break;
                        case 21:
                            Summon(8);
                            NextStep(0, false);
                            break;
                        case 22:
                            CaveDestruction(false);
                            Talk(SAY_BLASTMASTER_11);
                            NextStep(3000, true);
                            break;
                        case 23:
                            Summon(9);
                            Talk(SAY_BLASTMASTER_19);
                            NextStep(0, false);
                            break;
                    }
                }
                else uiTimer -= uiDiff;
            }

            UpdateVictim();
        }

        public override void JustSummoned(Creature summon)
        {
            SummonList.Add(summon.GetGUID());
            AggroAllPlayers(summon);
        }

    }
    [Script]
    class boss_grubbis : ScriptedAI
    {

        public boss_grubbis(Creature creature) : base(creature)
        {
            SetDataSummoner();
        }

        void SetDataSummoner()
        {
            if (!me.IsSummon())
                return;

            if (me.ToTempSummon().GetSummonerUnit() is Unit summon)
                if (summon.ToCreature() is Creature creature)
                    creature.GetAI().SetData(2, 1);
        }

        public override void JustDied(Unit killer)
        {
            if (!me.IsSummon())
                return;

            if (me.ToTempSummon().GetSummonerUnit() is Unit summoner)
                if (summoner.ToCreature() is Creature creature)
                    creature.GetAI().SetData(2, 2);
        }

    }
}
