using Framework.Constants;

namespace Game.AI
{
    public static class SISpellCastPolicy
    {
        // CheckPetCast sets the explicit unit target then invokes normal CheckCast. Player casts
        // do not require the separate pet auto-target hit-list heuristic, even against enemies.
        public static bool Accept(SpellCastResult check) => check == SpellCastResult.SpellCastOk;
    }
}
