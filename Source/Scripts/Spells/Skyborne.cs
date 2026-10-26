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

        void AttuneLeyLine(int effIndex)
        {
            var caster = GetCaster().ToPlayer();
            if (caster == null)
                return;

            // Replace the short buff before EffectTriggerSpell queues its cast.
            // Removing it in AfterCast is too early to remove that queued aura.
            bool nearby = caster.GetGameObjectListWithEntryInGrid(0, caster.GetVisibilityRange())
                .Any(go => go.GetGoInfo().GetSpellFocusType() == LeyLineFocus && go.IsSpawned()
                    && go.IsWithinDist(caster, go.GetGoInfo().GetSpellFocusRadius()));
            if (!nearby)
                return;

            PreventHitDefaultEffect(effIndex);
            caster.RemoveAurasDueToSpell(EnergizedShort);
            caster.CastSpell(caster, EnergizedLong,
                new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetTriggeringSpell(GetSpell()));
        }

        public override void Register()
        {
            OnEffectLaunch.Add(new(AttuneLeyLine, 0, SpellEffectName.TriggerSpell));
        }
    }
}
