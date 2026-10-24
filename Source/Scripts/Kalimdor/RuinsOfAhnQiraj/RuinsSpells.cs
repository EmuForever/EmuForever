// TrinityCore wotlk_classic scripts ported to CypherCore. GPL version 2 or later; see LICENSE.
using Framework.Constants;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;

namespace Scripts.Kalimdor.RuinsOfAhnQiraj
{
    [Script]
    class spell_egg_explosion : SpellScript
    {
        void HandleAfterCast() => GetCaster().FindNearestCreature(15370, 5.0f)?.GetAI().DoAction(0);
        void HandleDummyHitTarget(int effectIndex)
        {
            if (GetHitUnit() is Unit target)
            {
                CastSpellExtraArgs args = new(TriggerCastFlags.FullMask);
                args.AddSpellMod(SpellValueMod.BasePoint0, Math.Max(0, (int)(-16 * GetCaster().GetDistance(target) + 500)));
                GetCaster().CastSpell(target, 5255, args);
            }
        }
        public override void Register()
        {
            AfterCast.Add(new CastHandler(HandleAfterCast));
            OnEffectHitTarget.Add(new EffectHandler(HandleDummyHitTarget, 0, SpellEffectName.Dummy));
        }
    }

    [Script]
    class spell_rajaxx_thundercrash : SpellScript
    {
        void HandleDamageCalc(Unit victim, ref int damage, ref int flatMod, ref float pctMod)
            => damage = (int)victim.CountPctFromCurHealth(50);
        public override void Register() => CalcDamage.Add(new(HandleDamageCalc));
    }
}
