using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI;

public sealed partial class SIPlayer
{
    readonly ConcurrentQueue<Action<string>> _playerInfoRequests=new();
    internal bool HasPendingPlayerInfo=>_playerInfoRequests?.IsEmpty==false;
    public void RequestPlayerInfo(Action<string> reply)
    {
        if(_playerInfoRequests.Count<8)_playerInfoRequests.Enqueue(reply);
        else reply("An SI player-information request is already pending; try again shortly.");
    }
    void ReplyPlayerInfo()
    {
        if(!HasPendingPlayerInfo)return;
        string[] lines;
        try{lines=PlayerInformation();}catch(Exception e){lines=["SI player information unavailable: "+e.Message];}
        while(_playerInfoRequests.TryDequeue(out var reply))
            try{foreach(var line in lines)reply(line);}catch(Exception){/* The requesting session may have disconnected. */}
    }
    internal string[] PlayerInformation()
    {
        var lines=new List<string>();
        var here=Here;
        lines.Add($"{me.GetName()}: level {me.GetLevel()} {me.GetRace()} {me.GetClass()}; XP {me.GetXP()}/{me.GetXPForNextLevel()}; SI active for {_clock.TotalMinutes:F1} minutes.");
        lines.Add($"Location: map {me.GetMapId()}, zone {me.GetZoneId()}, area {me.GetAreaId()}, instance {me.GetInstanceId()}; ({here.X:F2}, {here.Y:F2}, {here.Z:F2}); facing {me.GetOrientation():F2}.");
        lines.Add($"Goal: {_goal}; behavior: {Behavior}; task: {_task?.Label??"none"}; status: {Status}.");
        lines.Add($"Previous: {LastBehaviorState}; next: {NextExpectedState}; recovery: {Recovery}.");
        lines.Add($"Health {me.GetHealth()}/{me.GetMaxHealth()} ({me.GetHealthPct():F1}%); mana {me.GetPower(PowerType.Mana)}/{me.GetMaxPower(PowerType.Mana)}; current power {me.GetPowerType()} {me.GetPower(me.GetPowerType())}/{me.GetMaxPower(me.GetPowerType())}.");
        lines.Add($"State: {(me.IsAlive()?"alive":"dead")}, combat {me.IsInCombat()}, mounted {me.IsMounted()}, swimming {me.IsInWater()}, taxi {me.IsInFlight()}, teleport {me.IsBeingTeleported()}; moving {!me.MoveSpline.Finalized()}.");
        lines.Add($"Stats: strength {me.GetStat(Stats.Strength):F0}, agility {me.GetStat(Stats.Agility):F0}, stamina {me.GetStat(Stats.Stamina):F0}, intellect {me.GetStat(Stats.Intellect):F0}, spirit {me.GetStat(Stats.Spirit):F0}; armor {me.GetArmor()}; melee/ranged AP {me.GetTotalAttackPowerValue(WeaponAttackType.BaseAttack):F0}/{me.GetTotalAttackPowerValue(WeaponAttackType.RangedAttack):F0}.");
        lines.Add("Resistances: "+string.Join(", ",new[]{SpellSchools.Holy,SpellSchools.Fire,SpellSchools.Nature,SpellSchools.Frost,SpellSchools.Shadow,SpellSchools.Arcane}.Select(s=>$"{s} {me.GetResistance(s)}"))+".");
        string who=_task?.Destination is SISpawn spawn?$"{(spawn.GameObject?ObjectName(spawn.Entry):CreatureName(spawn.Entry))} (entry {spawn.Entry}, spawn {spawn.SpawnId})":"";
        if(_encounter.Active&&Resolve(_encounter.Target) is Unit enemy)who=enemy.GetName()+" (combat target "+enemy.GetEntry()+")";
        else if(_task!=null&&!_task.Target.IsEmpty()&&Global.ObjAccessor.GetWorldObject(me,_task.Target) is WorldObject live)who=live.GetName()+" (entry "+live.GetEntry()+")";
        if(_hasMovementDestination)
        {
            var destination=_movementDestination.Point;
            lines.Add($"Navigating to {(who.Length==0?_journeyReason:who)}: map {_movementDestination.MapId} ({destination.X:F2}, {destination.Y:F2}, {destination.Z:F2}), {System.Numerics.Vector3.Distance(here,destination):F1} yards remaining; {NavigationMode}.");
        }
        else if(_task?.Destination is SISpawn planned)
            lines.Add($"Destination: {who}; map {planned.Map} ({planned.Position.X:F2}, {planned.Position.Y:F2}, {planned.Position.Z:F2}); {Distance(planned.Position):F1} yards; navigation {NavigationMode}.");
        else lines.Add($"Navigation: {NavigationMode}; destination: {(_task?.Hotspots.Count>0?"current quest/gathering area":"none")}; {_journeyReason}.");
        lines.Add($"Route: corner {_waypoint}/{_route.Length}; planning {_planning?.Status.ToString()??"none"}; next leg {_aheadMovement?.Status.ToString()??"none"}; walk/run/swim speed {me.GetSpeed(UnitMoveType.Walk).PerSec:F2}/{me.GetSpeed(UnitMoveType.Run).PerSec:F2}/{me.GetSpeed(UnitMoveType.Swim).PerSec:F2} yd/s.");
        if(_journeyFlight!=null)lines.Add($"Flight itinerary: {JourneyFlightDescription}; fare {_journeyFlight.Fare} copper; {(_journeyFlightTaken?"airborne":"boarding at "+CreatureName(_journeyFlight.Boarding.Entry))}; then {_journeyFlightReason} at ({_journeyFlightGoal.Point.X:F1}, {_journeyFlightGoal.Point.Y:F1}, {_journeyFlightGoal.Point.Z:F1}).");
        if(_routeWaypoint is System.Numerics.Vector3 point)lines.Add($"Next road point: ({point.X:F2}, {point.Y:F2}, {point.Z:F2}).");
        var cast=me.GetCurrentSpell(CurrentSpellTypes.Generic)??me.GetCurrentSpell(CurrentSpellTypes.Channeled);
        lines.Add($"Casting: {(cast==null?"none":SpellName(cast.GetSpellInfo())+" ("+cast.GetSpellInfo().Id+")")}; global cooldown: {(_lastRoutineSpell!=null&&me.GetSpellHistory().HasGlobalCooldown(_lastRoutineSpell))}.");
        var cooldowns=_knownSpells.Select(s=>(Spell:s,Remaining:me.GetSpellHistory().GetRemainingCooldown(s)))
            .Where(c=>c.Remaining>TimeSpan.Zero).Select(c=>$"{SpellName(c.Spell)} {c.Remaining.TotalSeconds:F1}s").ToArray();
        if(cooldowns.Length==0)lines.Add("Active spell cooldowns: none.");
        else foreach(var batch in cooldowns.Chunk(4))lines.Add("Active spell cooldowns: "+string.Join(", ",batch)+".");
        long copper=me.GetMoney();
        lines.Add($"Money: {copper/10000}g {copper/100%100}s {copper%100}c; free inventory slots {me.GetFreeInventorySlotCount()}; bank tabs {me.GetCharacterBankTabCount()}; food/water {SupplyCount(false)}/{SupplyCount(true)}; current repair cost {RequiredRepairCost()} copper.");
        lines.Add("Professions: "+string.Join(", ",KnownProfessions().Select(s=>$"{SkillName(s)} {me.GetSkillValue(s)}/{me.GetMaxSkillValue(s)}"))+".");
        lines.Add($"Profession work: {_professionSummary}; gathering target {(_gatherTarget.IsEmpty()?"none":_gatherTarget.ToString())}; node-update wait {Math.Max(0,_gatherSettleUntil-Now)} ms; trainer scan {(_professionTrainingProgressDue?"due after skill gains":Math.Max(0,_nextProfessionTrainingScan-Now)+" ms")}.");
        if(_professionBatchRoot!=null)lines.Add($"Profession batch: {_professionBatchRoot.Spell.SpellName[Locale.enUS]}; {_professionBatchCraftsLeft} crafts budgeted; skill target {_professionBatchSkillGoal}.");
        foreach(var batch in _professionGatherTargets.Chunk(4))lines.Add("Gather batch: "+string.Join(", ",batch.Select(p=>$"{ItemName(p.Key)} {me.GetItemCount(p.Key,true)}/{p.Value}"))+".");
        foreach(var batch in _professionSupplies.Chunk(4))lines.Add("Supplies needed: "+string.Join(", ",batch.Select(p=>$"{ItemName(p.Key)} {me.GetItemCount(p.Key)}/{p.Value}"))+".");
        var quests=QuestLog().ToArray();
        lines.Add($"Quests: {quests.Length} in log; lifetime completed/failed/abandoned {me.LifetimeQuestsCompleted}/{me.LifetimeQuestsFailed}/{me.LifetimeQuestsAbandoned}; SI accepted/completed {QuestsAccepted}/{QuestsCompleted}.");
        foreach(var (slot,quest) in quests)
            lines.Add($"Quest {quest.Id}: {quest.LogTitle} [{me.GetQuestStatus(quest.Id)}]; "+string.Join(", ",quest.Objectives.Select(o=>$"{o.Type} {me.GetQuestSlotObjectiveData(slot,o)}/{o.Amount}"))+".");
        lines.Add($"SI counters: kills {Kills}; damage dealt {DamageDelivered}; items looted {ItemsLooted}; loot animation {(_lootAnimationUntil>Now?"active":"none")}.");
        var group=me.GetGroup();
        lines.Add(group==null?"Party: none.":$"Party: {group.GetMembersCount()} members; following {PartyFollowing}; errand {PartyErrand}.");
        if(me.GetPet() is Pet pet)lines.Add($"Pet: {pet.GetName()}; health {pet.GetHealth()}/{pet.GetMaxHealth()}; reaction {pet.GetReactState()}; victim {pet.GetVictim()?.GetName()??"none"}.");
        return lines.ToArray();
    }
}
