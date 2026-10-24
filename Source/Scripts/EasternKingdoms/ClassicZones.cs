// TrinityCore wotlk_classic scripts ported to CypherCore. GPL version 2 or later; see LICENSE.
using Framework.Constants;
using Game.AI;
using Game.DataStorage;
using Game.Entities;
using Game.Groups;
using Game.Scripting;
using Game.Spells;
using System;

namespace Scripts.EasternKingdoms.ClassicZones
{
    [Script]
    class spell_razelikh_teleport_group : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo) => ValidateSpellInfo(12885, 13142);
        void HandleScriptEffect(int effIndex)
        {
            Player player = GetHitPlayer();
            if (player == null) return;
            Group group = player.GetGroup();
            if (group == null) { player.CastSpell(player, 12885, true); return; }
            for (GroupReference reference = group.GetFirstMember(); reference != null; reference = reference.Next())
            {
                Player member = reference.GetSource();
                if (member != null && member.IsWithinDistInMap(player, 20) && !member.IsDead()) member.CastSpell(member, 13142, true);
            }
        }
        public override void Register() => OnEffectHitTarget.Add(new(HandleScriptEffect, 0, SpellEffectName.ScriptEffect));
    }

    [Script]
    class spell_eastern_plaguelands_test_fetid_skull : SpellScript
    {
        public override bool Validate(SpellInfo spellInfo) => ValidateSpellInfo(17269, 17270);
        void HandleDummy(int effIndex) => GetCaster().CastSpell(GetCaster(), Random.Shared.Next(2) == 0 ? 17269 : 17270,
            new CastSpellExtraArgs(TriggerCastFlags.IgnorePowerAndReagentCost));
        public override void Register() => OnEffectHit.Add(new(HandleDummy, 0, SpellEffectName.Dummy));
    }

    [Script]
    class boss_twilight_corrupter : ScriptedAI
    {
        int _killCount;
        public boss_twilight_corrupter(Creature creature) : base(creature) { }
        public override void Reset() { _events.Reset(); _killCount = 0; }
        public override void JustEngagedWith(Unit who)
        {
            Talk(1);
            _events.ScheduleEvent(1, TimeSpan.FromSeconds(15));
            _events.ScheduleEvent(2, TimeSpan.FromSeconds(30));
        }
        public override void KilledUnit(Unit victim)
        {
            if (!victim.IsPlayer()) return;
            ++_killCount;
            Talk(2, victim);
            if (_killCount == 3) { DoCastSelf(24312, true); _killCount = 0; }
        }
        public override void UpdateAI(TimeSpan diff)
        {
            if (!UpdateVictim()) return;
            _events.Update(diff);
            if (me.HasUnitState(UnitState.Casting)) return;
            int eventId;
            while ((eventId = _events.ExecuteEvent()) != 0)
                switch (eventId)
                {
                    case 1: DoCastAOE(25805); _events.ScheduleEvent(1, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(19)); break;
                    case 2:
                        Unit target = SelectTarget(SelectTargetMethod.Random, 0, 0, true);
                        if (target != null) DoCast(target, 25806);
                        _events.ScheduleEvent(2, TimeSpan.FromSeconds(45));
                        break;
                }
        }
    }

    [Script]
    class at_twilight_grove : AreaTriggerScript
    {
        public at_twilight_grove() : base(nameof(at_twilight_grove)) { }
        public override bool OnTrigger(Player player, AreaTriggerRecord areaTrigger)
        {
            if (player.GetQuestStatus(8735) == QuestStatus.Incomplete && player.FindNearestCreature(15625, 500, true) == null)
            {
                Creature corrupter = player.SummonCreature(15625, new Position(-10328.16f, -489.57f, 49.95f), TempSummonType.ManualDespawn, TimeSpan.FromMinutes(1));
                corrupter?.GetAI().Talk(0, player);
            }
            return false;
        }
    }
}
