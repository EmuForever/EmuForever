using Framework.Constants;
using Game.Entities;
using Game.Spells;
using System;
using System.Linq;

namespace Game.AI;

public sealed partial class SIPlayer
{
    long _nextTravelRoot;
    bool ContinuePastTrivialAttackers()
    {
        if(!_hasMovementDestination||_movementFailed||_encounter.Active||_goal=="attack"||
            me.GetCombatManager().GetPvPCombatRefs().Count!=0)return false;
        var attackers=me.GetCombatManager().GetPvECombatRefs().Values.Select(r=>r.GetOther(me))
            .Concat(me.GetAttackers()).Where(u=>u.IsAlive()).DistinctBy(u=>u.GetGUID()).ToArray();
        if(attackers.Length==0||attackers.Any(u=>!SITravelCombatPolicy.Pass(me.GetLevel(),u.GetLevel(),u is Creature,true,false,false)))return false;
        me.InterruptSpell(CurrentSpellTypes.AutoRepeat);me.AttackStop();
        PacifyTravelPet();
        if(!me.IsMounted()&&Now>=_nextTravelRoot)
        {
            _nextTravelRoot=Now+3000;
            TryInstantTravelRoot(attackers.FirstOrDefault(u=>!u.HasAuraType(AuraType.ModRoot)));
        }
        _task?.Suspend(Now);
        Behavior="Travel";Status="Continuing past trivial attackers";
        // MaintainNavigation already owns the spline and pending route. Do not
        // clear/recreate it just because the native combat flag remains set.
        return true;
    }

    void TryInstantTravelRoot(Unit attacker)
    {
        if(attacker==null||me.IsMounted()||me.IsNonMeleeSpellCast(false,skipAutorepeat:true))return;
        foreach(var spell in _knownSpells.Where(s=>SIInstantAttackPolicy.Instant(s)&&s.HasAura(AuraType.ModRoot)))
        {
            bool targeted=spell.GetExplicitTargetMask().HasAnyFlag(SpellCastTargetFlags.UnitMask);
            Unit target=targeted?attacker:me;
            if(!targeted)
            {
                float radius=spell.GetEffects().Where(e=>e.IsAura(AuraType.ModRoot))
                    .Select(e=>Math.Max(e.CalcRadius(me),e.CalcRadius(me,SpellTargetIndex.TargetB))).DefaultIfEmpty(0).Max();
                if(radius<=0||me.GetExactDist(attacker)>radius)continue;
            }
            // Reject a root that would require turning/stopping the running
            // spline. Cast retains all ordinary cooldown/resource/LOS checks.
            if(new Spell(me,spell,TriggerCastFlags.None).CheckPetCast(target)!=SpellCastResult.SpellCastOk)continue;
            if(Cast(spell,target))return;
        }
    }
}
