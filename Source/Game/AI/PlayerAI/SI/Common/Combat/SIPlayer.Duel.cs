using Framework.Constants;
using System.Linq;

namespace Game.AI;
public sealed partial class SIPlayer
{
    bool _siDuelActive;
    long? _duelBuffBlockedAt;
    public static bool MayAcceptDuel(bool paused,bool dangerous)=>paused||!dangerous;

    bool TickDuel()
    {
        var duel=me.duel;
        if(duel==null||duel.State==DuelState.Completed)
        {
            if(_siDuelActive)
            {
                EndEncounter(false);CancelTravel();_siDuelActive=false;_duelRequestedAt=0;_duelBuffBlockedAt=null;
                _task?.Resume(Now);_preparation.Resume(Now);_nextReplan=0;
            }
            return false;
        }
        if(!me.IsAlive())return false;
        if(!_siDuelActive)
        {
            _siDuelActive=true;
            _duelBuffBlockedAt=null;
            CancelTravel();EndEncounter(false);_task?.Suspend(Now);_preparation.Pause(Now);
        }
        Behavior="Duel";
        if(duel.State==DuelState.Challenged)
        {
            if(TickOwnDuelRequest(duel))return true;
            bool danger=NearbyCreatures(_settings.TargetSearchRadius).Any(c=>c.IsAlive()&&
                me.IsValidAttackTarget(c)&&me.CanSeeOrDetect(c,distanceCheck:false));
            if(!MayAcceptDuel(_goal is "pause" or "duel",danger))
            {
                me.Say("Dangerous creatures around, I do not want to chance dying. It's a rough run back for me.",Language.Universal);
                me.GetSession().RespondToDuelForSI(false);Status="Declined unsafe duel";return true;
            }
            if(me.IsInCombat())
            {
                // In particular, paused SI players may still be challenged
                // while an actual attacker interrupts preparation. Defend first.
                TickCombatBehavior();Status="Defending before duel preparation";return true;
            }
            RefreshSpellBook();
            if(me.IsNonMeleeSpellCast(false,skipAutorepeat:true)){Status="Buffing before duel";return true;}
            // A failed/temporarily unaffordable cast is not evidence that all
            // buffs exist. Never accept just because a preparation timer expired.
            if(SelfBuffsPending())
            {
                if(Buff()){_duelBuffBlockedAt=null;Status="Applying all self-buffs before duel";return true;}
                // A warrior cannot regenerate the rage for a shout by sitting.
                // Use the learned native resource ability, then recover its HP
                // cost after the buffs have actually been applied.
                if(me.GetClass()==Class.Warrior&&_knownSpells.FirstOrDefault(s=>SpellName(s)=="bloodrage") is { } bloodrage&&Cast(bloodrage,me))
                {_duelBuffBlockedAt=null;Status="Generating rage for duel self-buffs";return true;}
                if(me.GetClass()==Class.Druid&&me.GetShapeshiftForm()!=ShapeShiftForm.None)
                {me.RemoveAurasByType(AuraType.ModShapeshift);Status="Returning to caster form for duel self-buffs";return true;}
                if(!DuelResourcesFull()){_duelBuffBlockedAt=null;RecoverForDuel();}
                else
                {
                    _duelBuffBlockedAt??=Now;
                    if(SISelfBuffPolicy.DuelBlocked(Now,_duelBuffBlockedAt.Value))
                    {
                        me.GetSession().RespondToDuelForSI(false);
                        Status="Duel declined: a required self-buff could not be applied";
                        Report("duel_buff_blocked",Status);return true;
                    }
                }
                Status="Waiting to finish self-buffs before duel";return true;
            }
            if(!DuelResourcesFull())
            {RecoverForDuel();Status="Recovering to full health and mana before duel";return true;}
            StandUp();
            _resting=false;_recovering=false;
            me.GetSession().RespondToDuelForSI(true);Status="Accepted duel";return true;
        }
        if(duel.State==DuelState.Countdown){Status="Duel countdown";return true;}
        if(duel.State==DuelState.InProgress)
        {
            RefreshSpellBook();SelectAmmunition();
            if(!_encounter.Active||_encounter.Target!=duel.Opponent.GetGUID())BeginEncounter(duel.Opponent);
            TickEncounter();return true;
        }
        return true;
    }

    bool DuelResourcesFull()=>me.GetHealth()>=me.GetMaxHealth()&&
        (me.GetMaxPower(PowerType.Mana)==0||me.GetPower(PowerType.Mana)>=me.GetMaxPower(PowerType.Mana));

    void RecoverForDuel()
    {
        CancelTravel();
        // Duel preparation deliberately tops off above the normal 50% food /
        // drink threshold. Both can run together through native item cooldowns.
        if(HasUsefulRecoveryAura(fullRecovery:true)){TryRecoveryConsumable(fullRecovery:true);return;}
        if(TryRecoveryConsumable(fullRecovery:true))return;
        if(me.GetHealth()<me.GetMaxHealth())
            foreach(var spell in _knownSpells.Where(IsHeal))if(Cast(spell,me))return;
        SitDown();
    }
}
