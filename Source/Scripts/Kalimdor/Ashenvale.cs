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
using Game;
using Game.Entities;
using Game.Maps;
using Game.Scripting;
using System;

namespace Scripts.Kalimdor.Ashenvale
{
    [Script]
    class npc_ruul_snowhoof : EscortAI
    {
        static readonly Position[] Summons =
        {
            new(3449.218018f, -587.825073f, 174.978867f, 4.714445f),
            new(3446.384521f, -587.830872f, 175.186279f, 4.714445f),
            new(3444.218994f, -587.835327f, 175.380600f, 4.714445f),
            new(3508.344482f, -492.024261f, 186.929031f, 4.145029f),
            new(3506.265625f, -490.531006f, 186.740128f, 4.239277f),
            new(3503.682373f, -489.393799f, 186.629684f, 4.349232f)
        };
        public npc_ruul_snowhoof(Creature creature) : base(creature) { }
        public override void Reset() => me.FindNearestGameObject(178147, 20)?.SetGoState(GameObjectState.Ready);
        public override void JustEngagedWith(Unit who) { }
        public override void JustSummoned(Creature summoned) => summoned.GetAI().AttackStart(me);
        public override void OnQuestAccept(Player player, Quest quest)
        {
            if (quest.Id != 6482) return;
            me.SetFaction(113);
            LoadPath(102546);
            Start(true, player.GetGUID());
        }
        public override void WaypointReached(int waypointId, int pathId)
        {
            Player player = GetPlayerForEscort();
            if (player == null) return;
            switch (waypointId)
            {
                case 0:
                    me.SetStandState(UnitStandStateType.Stand);
                    me.FindNearestGameObject(178147, 20)?.SetGoState(GameObjectState.Active);
                    break;
                case 13: SummonWave(0); break;
                case 19: SummonWave(3); break;
                case 27:
                    me.SetFaction(me.GetCreatureTemplate().Faction);
                    me.RemoveAurasDueToSpell(20514);
                    Talk(0, player);
                    player.GroupEventHappens(6482, me);
                    break;
            }
        }
        void SummonWave(int offset)
        {
            me.SummonCreature(3922, Summons[offset], TempSummonType.DeadDespawn, TimeSpan.FromMinutes(1));
            me.SummonCreature(3921, Summons[offset + 1], TempSummonType.DeadDespawn, TimeSpan.FromMinutes(1));
            me.SummonCreature(3926, Summons[offset + 2], TempSummonType.DeadDespawn, TimeSpan.FromMinutes(1));
        }
        public override void EnterEvadeMode(EvadeReason why = EvadeReason.Other)
        {
            if (!me.HasAura(20514)) me.AddAura(20514, me);
            base.EnterEvadeMode(why);
        }
    }

    [Script]
    class npc_muglash : EscortAI
    {
        TimeSpan _eventTimer;
        int _wave;
        public bool IsBrazierExtinguished;
        public npc_muglash(Creature creature) : base(creature) => Initialize();
        void Initialize() { _eventTimer = TimeSpan.FromSeconds(10); _wave = 0; IsBrazierExtinguished = false; }
        public override void Reset() => Initialize();
        public override void JustEngagedWith(Unit who)
        {
            Player player = GetPlayerForEscort();
            if (player != null && HasEscortState(EscortState.Paused) && Random.Shared.Next(2) != 0)
                Talk(4, player);
        }
        public override void JustDied(Unit killer)
        {
            if (HasEscortState(EscortState.Escorting)) GetPlayerForEscort()?.FailQuest(6641);
        }
        public override void JustSummoned(Creature summoned) => summoned.GetAI().AttackStart(me);
        public override void OnQuestAccept(Player player, Quest quest)
        {
            if (quest.Id != 6641) return;
            Talk(0);
            me.SetFaction(113);
            LoadPath(101738);
            Start(true, player.GetGUID());
        }
        public override void WaypointReached(int waypointId, int pathId)
        {
            Player player = GetPlayerForEscort();
            if (player == null) return;
            switch (waypointId)
            {
                case 0: Talk(1, player); break;
                case 24:
                    Talk(2, player);
                    GameObject brazier = me.FindNearestGameObject(178247, SharedConst.InteractionDistance * 2);
                    if (brazier != null)
                    {
                        brazier.RemoveFlag(GameObjectFlags.NotSelectable);
                        SetEscortPaused(true);
                    }
                    break;
                case 25: Talk(7); player.GroupEventHappens(6641, me); break;
                case 26: Talk(8); break;
                case 27: Talk(9); break;
            }
        }
        void Summon(int entry, float x, float y, float z) =>
            me.SummonCreature(entry, new Position(x, y, z), TempSummonType.TimedDespawnOutOfCombat, TimeSpan.FromMinutes(1));
        void SummonWave()
        {
            switch (++_wave)
            {
                case 1:
                    Summon(3713, 3603.504150f, 1122.631104f, 1.635f);
                    Summon(3717, 3589.293945f, 1148.664063f, 5.565f);
                    Summon(3712, 3609.925537f, 1168.759521f, -1.168f);
                    break;
                case 2:
                    Summon(3944, 3609.925537f, 1168.759521f, -1.168f);
                    Summon(3711, 3645.652100f, 1139.425415f, 1.322f);
                    Summon(3715, 3583.602051f, 1128.405762f, 2.347f);
                    break;
                case 3: Summon(12940, 3633.056885f, 1172.924072f, -5.388f); break;
                case 4: SetEscortPaused(false); Talk(6); break;
            }
        }
        public override void UpdateAI(TimeSpan diff)
        {
            base.UpdateAI(diff);
            if (me.GetVictim() == null)
            {
                if (HasEscortState(EscortState.Paused) && IsBrazierExtinguished)
                {
                    if (_eventTimer < diff) { SummonWave(); _eventTimer = TimeSpan.FromSeconds(10); }
                    else _eventTimer -= diff;
                }
                return;
            }
        }
    }

    [Script]
    class go_naga_brazier : GameObjectAI
    {
        public go_naga_brazier(GameObject go) : base(go) { }
        public override bool OnGossipHello(Player player)
        {
            Creature muglash = me.FindNearestCreature(12717, SharedConst.InteractionDistance * 2);
            if (muglash?.GetAI() is npc_muglash escort)
            {
                escort.Talk(3);
                escort.IsBrazierExtinguished = true;
                return false;
            }
            return true;
        }
    }

    [Script]
    class spell_destroy_karangs_banner : SpellScript
    {
        void HandleAfterCast() => GetCaster().FindNearestGameObject(178205, GetSpellInfo().GetMaxRange(true))?.Delete();
        public override void Register() => AfterCast.Add(new(HandleAfterCast));
    }
}
