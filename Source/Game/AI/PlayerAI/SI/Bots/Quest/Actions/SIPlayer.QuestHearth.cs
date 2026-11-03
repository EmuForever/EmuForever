using Framework.Constants;
using Game.Entities;
using System;
using System.Linq;
using System.Numerics;

namespace Game.AI;

public sealed partial class SIPlayer
{
    long _nextQuestHearthCheck;
    long? _questHearthQueuedAt;

    // A home bind is useful only when it actually puts quest work within reach.
    // Do not spend a Hearthstone for a short walk or return to an exhausted hub.
    internal static bool QuestHearthImprovesRoute(int map,Vector3 here,int homeMap,Vector3 home,
        int workMap,Vector3 work,float radius) =>
        homeMap==workMap&&Vector2.Distance(new(home.X,home.Y),new(work.X,work.Y))<=radius&&
        (map!=homeMap||Vector2.Distance(new(here.X,here.Y),new(home.X,home.Y))>=Math.Max(1500,radius*2)&&
            Vector2.Distance(new(here.X,here.Y),new(work.X,work.Y))>=
                Vector2.Distance(new(home.X,home.Y),new(work.X,work.Y))+500);

    bool ContinueQuestHearth()
    {
        if(_questHearthQueuedAt is not long queued)return false;
        if(_goal!="quest"||!me.IsAlive()||me.IsInCombat()||_encounter.Active)
        { _questHearthQueuedAt=null;return false; }
        // Item use is queued through the session. Give it time to start, then let
        // failed/interrupted casts release the bot instead of leaving it idle.
        if(Now-queued>2000&&!me.IsNonMeleeSpellCast(false,skipAutorepeat:true))
        { _questHearthQueuedAt=null;return false; }
        Behavior="Hearthstone";Status="Hearthing back to quest work";
        _task?.Suspend(Now);return true;
    }

    bool TryQuestHearth()
    {
        if(_goal!="quest"||!SIQuestKnowledge.IsBuilt||!_preparation.Complete||Now<_nextQuestHearthCheck||
            !me.IsAlive()||me.IsInCombat()||_encounter.Active||me.InBattleground()||me.GetMap().IsDungeon()||
            me.IsInFlight()||me.IsBeingTeleported()||me.GetTransport()!=null||
            me.IsNonMeleeSpellCast(false,skipAutorepeat:true)||OwnedServiceTrip||_auctionMailDestination.HasValue)return false;
        if(_task!=null&&(_task.Quest==null||!TaskDone(_task)&&
            (TaskDistance(_task)<=_settings.TargetSearchRadius||CommittedToObjective())))return false;
        _nextQuestHearthCheck=Now+30000+(long)(me.GetGUID().GetCounter()%5000);
        var home=me.GetHomebind();
        float radius=QuestPickupRadius;
        if(me.GetMapId()==home.GetMapId()&&Distance(V(home))<Math.Max(1500,radius*2))return false;
        bool NearHome(int map,Vector3 point)=>QuestHearthImprovesRoute(me.GetMapId(),Here,home.GetMapId(),V(home),map,point,radius);
        // No synthetic teleport or cooldown reset: use the inventory item and
        // the same validation/cast path as a player's Hearthstone.
        var item=me.GetItemByEntry(6948,ItemSearchLocation.Inventory);
        if(item==null||me.CanUseItem(item)!=InventoryResult.Ok)return false;
        var spell=Global.SpellMgr.GetSpellInfo(8690,me.GetMap().GetDifficultyID());
        if(spell==null||!item.GetEffects().Any(e=>e.TriggerType==ItemSpelltriggerType.OnUse&&e.SpellID==spell.Id)||
            !me.GetSpellHistory().IsReady(spell,item.GetEntry()))return false;
        int homeZone=Global.TerrainMgr.GetZoneId(me.GetPhaseShift(),home);
        bool Usable(SISpawn spawn,bool friendly)=>NearHome(spawn.Map,spawn.Position)&&
            (homeZone==0||Global.TerrainMgr.GetZoneId(me.GetPhaseShift(),spawn.Map,P(spawn.Position))==homeZone)&&
            (!friendly||spawn.GameObject||FriendlyTemplate(spawn.Entry));
        string reason=null;
        foreach(var (slot,quest) in QuestLog())
        {
            if(CollectsCloth(quest)||!QuestLevelEligible(quest))continue;
            if(me.GetQuestStatus(quest.Id)==QuestStatus.Complete)
            {
                if(Knowledge.Spawns(Knowledge.CreatureEnders(quest.Id),Knowledge.ObjectEnders(quest.Id),home.GetMapId()).Any(s=>Usable(s,true)))
                { reason="Turn in "+quest.LogTitle;break; }
            }
            else if(me.GetQuestStatus(quest.Id)==QuestStatus.Incomplete)
            {
                foreach(var objective in Knowledge.Plan(quest).Objectives)
                {
                    if(!objective.Supported||me.IsQuestObjectiveComplete(slot,quest,objective.Objective))continue;
                    if(Knowledge.Spawns(objective.Creatures.Concat(objective.Vendors),objective.Objects,home.GetMapId()).Any(s=>Usable(s,false))||
                        objective.Triggers.Any(t=>NearHome(t.ContinentID,t.Pos)))
                    { reason="Continue "+quest.LogTitle;break; }
                }
                if(reason!=null)break;
            }
        }
        if(reason==null)
            foreach(int id in Knowledge.QuestsStartedOnMap(home.GetMapId()))
            {
                var quest=Global.ObjectMgr.GetQuestTemplate(id);
                if(quest==null||me.GetQuestStatus(id)!=QuestStatus.None||!QuestLevelEligible(quest))continue;
                if(!Knowledge.Spawns(Knowledge.CreatureStarters(id),Knowledge.ObjectStarters(id),home.GetMapId()).Any(s=>Usable(s,true))||!WantsQuest(quest))continue;
                reason="Pick up "+quest.LogTitle;break;
            }
        if(reason==null)return false;
        ReleaseJourney();CancelTravel();Dismount();me.SetStandState(UnitStandStateType.Stand);
        if(!me.GetSession().UseItemForSI(item,spell.Id))return false;
        _questHearthQueuedAt=Now;
        Report("quest_hearth",$"Returning to home bind on map {home.GetMapId()}: {reason}");
        return ContinueQuestHearth();
    }
}
