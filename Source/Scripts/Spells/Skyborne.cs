using Framework.Constants;
using Game.Spells;
using Game.Scripting;
using System.Linq;

namespace Scripts.Spells.Generic
{
    [Script] // 1259705 - Read Ley Line
    class classic_spell_skyborne_read_ley_line : SpellScript
    {
        const int LeyLineFocus = 2246;
        const int EnergizedShort = 1270842;
        const int EnergizedLong = 1259691;

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(EnergizedShort, EnergizedLong);
        }

        void AttuneLeyLine()
        {
            var caster = GetCaster().ToPlayer();
            if (caster == null)
                return;

            // Use the focus and radius configured on the ley-line objects. The
            // client spell supplies the 15-second fallback when no focus is nearby.
            bool nearby = caster.GetGameObjectListWithEntryInGrid(0, caster.GetVisibilityRange())
                .Any(go => go.GetGoInfo().GetSpellFocusType() == LeyLineFocus && go.IsSpawned()
                    && go.IsWithinDist(caster, go.GetGoInfo().GetSpellFocusRadius()));
            if (nearby && caster.CastSpell(caster, EnergizedLong, TriggerCastFlags.FullMask) == SpellCastResult.SpellCastOk)
                caster.RemoveAurasDueToSpell(EnergizedShort);
        }

        public override void Register()
        {
            AfterCast.Add(new(AttuneLeyLine));
        }
    }
}
