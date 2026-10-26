using Framework.Constants;
using Game.AI;
using Game.Entities;
using Game.Scripting;

namespace Scripts.World.Areatriggers
{
    // Energizing Vortex (267599): Energizing Winds (1299003), create properties 43253.
    // Mirrors classic_at_energizing_vortex in the Trinity Classic reference.
    [Script]
    class classic_at_energizing_vortex : AreaTriggerAI
    {
        const int BlessingOfZephras = 1258510;

        public classic_at_energizing_vortex(AreaTrigger areaTrigger) : base(areaTrigger) { }

        public override void OnUnitEnter(Unit unit)
        {
            if (unit.ToPlayer() is Player player && !player.IsInCombat())
                player.CastSpell(player, BlessingOfZephras, TriggerCastFlags.FullMask);
        }
    }
}
