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
using Game;
using Game.AI;
using Game.Entities;
using Game.Maps;
using Game.Scripting;
using System;
using System.Linq;

namespace Scripts.Kalimdor.RazorfenKraul
{
    [Script]
    class instance_razorfen_kraul : InstanceMapScript
    {
        public instance_razorfen_kraul() : base(nameof(instance_razorfen_kraul), 47) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new RazorfenKraulInstance(map);
        class RazorfenKraulInstance : InstanceScript
        {
            ObjectGuid _ward;
            int _wardKeeperDeaths;
            public RazorfenKraulInstance(InstanceMap map) : base(map)
            {
                SetHeaders("RFK");
                SetBossNumber(5);
                LoadDungeonEncounterData(new DungeonEncounterData[] { new(0, 1656), new(1, 438), new(2, 1659), new(3, 1660), new(4, 1661) });
            }
            public override void OnUnitDeath(Unit unit)
            {
                switch (unit.GetEntry())
                {
                    case 75001: SetBossState(0, EncounterState.Done); break;
                    case 6168: SetBossState(1, EncounterState.Done); break;
                    case 74462: SetBossState(2, EncounterState.Done); break;
                    case 75247: SetBossState(3, EncounterState.Done); break;
                    case 4421: SetBossState(4, EncounterState.Done); break;
                }
            }
            public override void OnGameObjectCreate(GameObject go)
            {
                if (go.GetEntry() == 21099) _ward = go.GetGUID();
                else if (go.GetEntry() == 20920) go.SetFaction(0);
            }
            public override void Update(TimeSpan diff)
            {
                if (_wardKeeperDeaths != 2) return;
                GameObject ward = instance.GetGameObject(_ward);
                if (ward == null) return;
                ward.SetFlag(GameObjectFlags.InUse | GameObjectFlags.NoDespawn);
                ward.SetGoState(GameObjectState.Active);
            }
            public override void SetData(int type, int data)
            {
                if (type == 1) ++_wardKeeperDeaths;
            }
        }
    }

    [Script]
    class npc_willix : EscortAI
    {
        public npc_willix(Creature creature) : base(creature) { }
        public override void OnQuestAccept(Player player, Quest quest)
        {
            if (quest.Id != 1144) return;
            LoadPath(36066);
            Start(true, player.GetGUID());
            Talk(0, player);
            me.SetFaction(113);
        }
        void SummonBoar(float x, float y, float z) => me.SummonCreature(4514, new Position(x, y, z, 1.54f), TempSummonType.TimedDespawnOutOfCombat, TimeSpan.FromSeconds(25));
        public override void WaypointReached(int waypointId, int pathId)
        {
            Player player = GetPlayerForEscort();
            if (player == null) return;
            switch (waypointId)
            {
                case 3: me.HandleEmoteCommand(Emote.StatePoint); Talk(1, player); break;
                case 4: SummonBoar(2137.66f, 1843.98f, 48.08f); break;
                case 8: Talk(3, player); break;
                case 9: Talk(4, player); break;
                case 13: Talk(5, player); break;
                case 14: SummonBoar(2078.91f, 1704.54f, 56.77f); break;
                case 25: Talk(6, player); break;
                case 31: Talk(7, player); break;
                case 42: Talk(8, player); break;
                case 43: SummonBoar(1956.43f, 1596.97f, 81.75f); break;
                case 45:
                    Talk(9, player);
                    me.SetNpcFlag(NPCFlags1.QuestGiver);
                    player.GroupEventHappens(1144, me);
                    break;
                case 46: Talk(10, player); break;
            }
        }
        public override void Reset() { }
        public override void JustEngagedWith(Unit who) => Talk(2, who);
        public override void JustSummoned(Creature summoned) => summoned.GetAI().AttackStart(me);
        public override void JustDied(Unit killer) => GetPlayerForEscort()?.FailQuest(1144);
    }

    [Script]
    class npc_snufflenose_gopher : PetAI
    {
        bool _movementActive;
        ObjectGuid _targetTubber;
        public npc_snufflenose_gopher(Creature creature) : base(creature) { }
        public override void Reset() { _movementActive = false; _targetTubber.Clear(); }
        public override void MovementInform(MovementGeneratorType type, int id)
        {
            if (type != MovementGeneratorType.Point || id != 0) { base.MovementInform(type, id); return; }
            GameObject tubber = ObjectAccessor.GetGameObject(me, _targetTubber);
            if (tubber != null)
            {
                tubber.SetRespawnTime(TimeSpan.FromMinutes(5));
                tubber.Refresh();
                tubber.RemoveFlag(GameObjectFlags.InteractCond);
            }
            _movementActive = false;
        }
        public override void DoAction(int action)
        {
            if (action != 0) return;
            GameObject nearest = me.GetGameObjectListWithEntryInGrid(20920, 40)
                .Where(go => !go.IsSpawned() && go.HasFlag(GameObjectFlags.InteractCond))
                .OrderBy(go => me.GetDistance(go)).FirstOrDefault();
            // The reference calls front() after filtering; handle an empty result safely.
            if (nearest == null) return;
            _targetTubber = nearest.GetGUID();
            me.GetMotionMaster().MovePoint(0, nearest.GetPositionX(), nearest.GetPositionY(), nearest.GetPositionZ());
            _movementActive = true;
        }
        public override void UpdateAI(TimeSpan diff)
        {
            if (!_movementActive) base.UpdateAI(diff);
        }
    }

    [Script]
    class spell_snufflenose_command : SpellScript
    {
        void HandleEffect(int effIndex)
        {
            Creature target = GetHitCreature();
            if (target != null && target.GetEntry() == 4781) target.GetAI().DoAction(0);
        }
        public override void Register() => OnEffectHitTarget.Add(new(HandleEffect, 0, SpellEffectName.Dummy));
    }
}
