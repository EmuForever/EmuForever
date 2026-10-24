// TrinityCore wotlk_classic scripts ported to CypherCore. GPL version 2 or later; see LICENSE.
using Framework.Constants;
using Game.AI;
using Game.DataStorage;
using Game.Entities;
using Game.Maps;
using Game.Scripting;
using Game.Spells;
using System;
using System.Numerics;

namespace Scripts.EasternKingdoms.SunkenTemple
{
    [Script]
    class instance_sunken_temple : InstanceMapScript
    {
        public instance_sunken_temple() : base(nameof(instance_sunken_temple), 109) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new SunkenTempleInstance(map);
        class SunkenTempleInstance : InstanceScript
        {
            readonly ObjectGuid[] _statues = new ObjectGuid[6];
            int _state, _nextStatue;
            public SunkenTempleInstance(InstanceMap map) : base(map)
            {
                SetHeaders("ST");
                SetBossNumber(7);
                LoadDungeonEncounterData(new DungeonEncounterData[]
                {
                    new(0,492), new(1,488), new(2,486), new(3,487), new(4,490), new(5,491), new(6,493)
                });
            }
            public override void OnUnitDeath(Unit unit)
            {
                int boss = unit.GetEntry() switch { 8443 => 0, 5710 => 1, 5721 => 2, 5720 => 3, 5719 => 4, 5722 => 5, 5709 => 6, _ => -1 };
                if (boss >= 0) SetBossState(boss, EncounterState.Done);
            }
            public override void OnGameObjectCreate(GameObject go)
            {
                if (go.GetEntry() >= 148830 && go.GetEntry() <= 148835) _statues[go.GetEntry() - 148830] = go.GetGUID();
            }
            public override void Update(TimeSpan diff)
            {
                if (_nextStatue >= 6 || _state != 148830 + _nextStatue) return;
                GameObject statue = instance.GetGameObject(_statues[_nextStatue]);
                if (statue != null)
                {
                    statue.SummonGameObject(148883, statue.GetPosition(), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, statue.GetOrientation()), TimeSpan.Zero);
                    statue.SetFlag(GameObjectFlags.InteractCond);
                }
                ++_nextStatue;
                _state = 0;
            }
            public override void SetData(int type, int data) { if (type == 1) _state = data; }
            public override int GetData(int type) => type == 1 ? _state : 0;
        }
    }

    [Script]
    class at_malfurion_stormrage : AreaTriggerScript
    {
        public at_malfurion_stormrage() : base(nameof(at_malfurion_stormrage)) { }
        public override bool OnTrigger(Player player, AreaTriggerRecord areaTrigger)
        {
            if (player.GetInstanceScript() != null && player.FindNearestCreature(15362, 15) == null &&
                player.GetQuestStatus(8555) == QuestStatus.Rewarded && player.GetQuestStatus(8733) != QuestStatus.Rewarded)
                player.SummonCreature(15362, new Position(player.GetPositionX(), player.GetPositionY(), player.GetPositionZ(), -1.52f), TempSummonType.TimedOrDeadDespawn, TimeSpan.FromSeconds(100));
            return false;
        }
    }

    [Script]
    class go_atalai_statue : GameObjectAI
    {
        readonly InstanceScript _instance;
        public go_atalai_statue(GameObject go) : base(go) => _instance = go.GetInstanceScript();
        public override bool OnGossipHello(Player player) { _instance.SetData(1, me.GetEntry()); return false; }
    }

    [Script]
    class spell_sunken_temple_hex_of_jammalan : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo) => ValidateSpellInfo(12480, 12483);
        void AfterRemove(AuraEffect effect, AuraEffectHandleModes mode)
        {
            if (GetTargetApplication().GetRemoveMode() != AuraRemoveMode.Expire) return;
            Unit caster = GetCaster();
            if (caster == null || !caster.IsAlive()) return;
            caster.CastSpell(GetTarget(), 12480, true);
            caster.CastSpell(GetTarget(), 12483, true);
        }
        public override void Register() => AfterEffectRemove.Add(new(AfterRemove, 0, AuraType.Dummy, AuraEffectHandleModes.Real));
    }

    [Script]
    class spell_sunken_temple_hex_of_jammalan_transform : AuraScript
    {
        public override bool Validate(SpellInfo spellInfo) => ValidateSpellInfo(12483);
        void AfterRemove(AuraEffect effect, AuraEffectHandleModes mode) => GetTarget().RemoveAurasDueToSpell(12483);
        public override void Register() => AfterEffectRemove.Add(new(AfterRemove, 0, AuraType.Transform, AuraEffectHandleModes.Real));
    }
}
