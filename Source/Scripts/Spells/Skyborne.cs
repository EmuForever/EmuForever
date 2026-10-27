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

    [Script] // 1259686 - Skysight: next to an Elemental Convergence (focus 2271) the 15 min Elemental Blessing replaces the 30 sec one
    class classic_spell_skyborne_skysight : SpellScript
    {
        const int ElementalConvergenceFocus = 2271;
        const int ElementalBlessingShort = 1259688;
        const int ElementalBlessingLong = 1270893;

        public override bool Validate(SpellInfo spellInfo)
        {
            return ValidateSpellInfo(ElementalBlessingShort, ElementalBlessingLong);
        }

        void HandleTrigger(int effIndex)
        {
            var caster = GetCaster().ToPlayer();
            if (caster == null)
                return;

            bool nearby = caster.GetGameObjectListWithEntryInGrid(0, caster.GetVisibilityRange())
                .Any(go => go.GetGoInfo().GetSpellFocusType() == ElementalConvergenceFocus && go.IsSpawned()
                    && go.IsWithinDist(caster, go.GetGoInfo().GetSpellFocusRadius()));
            if (!nearby)
                return;

            PreventHitDefaultEffect(effIndex);
            caster.RemoveAurasDueToSpell(ElementalBlessingShort);
            caster.CastSpell(caster, ElementalBlessingLong,
                new CastSpellExtraArgs(TriggerCastFlags.FullMask).SetTriggeringSpell(GetSpell()));
        }

        public override void Register()
        {
            OnEffectLaunch.Add(new(HandleTrigger, 0, SpellEffectName.TriggerSpell));
        }
    }
}
