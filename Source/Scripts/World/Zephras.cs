using Framework.Constants;
using Game.AI;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;
using System.Collections.Generic;

namespace Scripts.World.Areatriggers
{
    [Script]
    class player_zephras_blessing : PlayerScript
    {
        public player_zephras_blessing() : base("player_zephras_blessing") { }

        static bool IsHostileSpell(Spell spell)
        {
            if (!spell.IsPositive())
                return true;

            // Dispel spells can be helpful or hostile depending on the target.
            Unit target = spell.m_targets.GetUnitTarget();
            return spell.GetSpellInfo().HasEffect(SpellEffectName.Dispel) && target != null
                && spell.GetCaster().ToUnit()?.IsFriendlyTo(target) == false;
        }

        public override void OnSpellCast(Player player, Spell spell, bool skipCheck)
        {
            if (player.HasAura(1258510) && IsHostileSpell(spell))
                player.RemoveAurasDueToSpell(1258510);
        }
    }

    // Keep the visual area's callback on the same creature-owned pickup logic.
    [Script]
    class classic_at_energizing_vortex : AreaTriggerAI
    {
        public classic_at_energizing_vortex(AreaTrigger areaTrigger) : base(areaTrigger) { }

        public override void OnUnitEnter(Unit unit)
        {
            if (unit.ToPlayer() is Player player && !player.IsInCombat()
                && at.GetCaster()?.ToCreature()?.GetAI() is classic_npc_energizing_vortex vortex)
                vortex.TryTouch(player);
        }
    }

    // The pickup belongs to the creature, independently of its attached visual area.
    [Script]
    class classic_npc_energizing_vortex : SmartAI
    {
        const int BlessingOfZephras = 1258510;
        const float PickupRadius = 3.0f;
        readonly HashSet<ObjectGuid> _inside = new();
        TimeSpan _checkTimer;

        public classic_npc_energizing_vortex(Creature creature) : base(creature) { }

        public override void Reset()
        {
            base.Reset();
            _inside.Clear();
            _checkTimer = TimeSpan.Zero;
        }

        public override void MoveInLineOfSight(Unit who)
        {
            base.MoveInLineOfSight(who);
            TryTouch(who);
        }

        public void TryTouch(Unit unit)
        {
            if (unit?.ToPlayer() is not Player player)
                return;

            // Do not expand the yard by either unit's combat reach/model size.
            if (!me.IsAlive() || !player.IsAlive()
                || !me.IsWithinDistInMap(player, PickupRadius, true, false, false))
            {
                _inside.Remove(player.GetGUID());
                return;
            }

            bool entered = _inside.Add(player.GetGUID());
            if (entered && !player.IsInCombat()
                && player.CastSpell(player, BlessingOfZephras, TriggerCastFlags.FullMask) != SpellCastResult.SpellCastOk)
                _inside.Remove(player.GetGUID()); // retry a failed pickup, not a deliberately broken buff
        }

        public override void UpdateAI(TimeSpan diff)
        {
            base.UpdateAI(diff);
            if (!me.IsAlive() || !me.IsInWorld)
                return;

            if (_checkTimer > diff)
            {
                _checkTimer -= diff;
                return;
            }
            _checkTimer = TimeSpan.FromMilliseconds(100);

            HashSet<ObjectGuid> current = new();
            foreach (Unit unit in me.GetPlayerListInGrid(PickupRadius))
            {
                TryTouch(unit);
                if (me.IsWithinDistInMap(unit, PickupRadius, true, false, false))
                    current.Add(unit.GetGUID());
            }
            _inside.IntersectWith(current);
        }
    }
}
