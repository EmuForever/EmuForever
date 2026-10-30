using Framework.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Game.AI;

public sealed partial class SIPlayer
{
    readonly Dictionary<int,int> _acceptedQuestZones=new();
    readonly HashSet<int> _authorizedQuestZoneTrips=new();
    readonly Dictionary<(int Map,Vector3 Point),int> _questPointZones=new();
    long _questZonePhase;
    int QuestPointZone(Vector3 point)
    {
        if(me.GetZoneId()==0)return 0;
        long context=MovementContext();
        if(_questZonePhase!=context){_questZonePhase=context;_questPointZones.Clear();}
        var key=(me.GetMapId(),point);
        if(!_questPointZones.TryGetValue(key,out int zone))
            _questPointZones[key]=zone=me.GetMap().GetZoneId(me.GetPhaseShift(),point.X,point.Y,point.Z);
        return zone;
    }
    int QuestOriginZone(Quest quest)=>_acceptedQuestZones.TryGetValue(quest.Id,out int origin)?origin:
        quest.QuestSortID>0?quest.QuestSortID:me.GetZoneId();
    bool TaskInCurrentZone(SITask task)=>task.Destination is SISpawn spawn?QuestPointZone(spawn.Position)==me.GetZoneId():
        task.Hotspots.Any(p=>QuestPointZone(p)==me.GetZoneId());
    bool QuestDestinationAllowed(SITask task)
    {
        if(task.Quest==null||me.GetZoneId()==0)return true;
        int zone=task.Destination is SISpawn spawn?QuestPointZone(spawn.Position):task.Hotspots.Count>0?QuestPointZone(task.Hotspots[0]):me.GetZoneId();
        return SIQuestZonePolicy.Allows(me.GetZoneId(),zone,QuestOriginZone(task.Quest),
            me.GetQuestStatus(task.Quest.Id) is QuestStatus.Incomplete or QuestStatus.Complete,
            _authorizedQuestZoneTrips.Contains(task.Quest.Id),task.Kind==SITaskKind.TurnIn);
    }
    void AuthorizeQuestTravel(SITask task)
    {
        if(me.GetZoneId()!=0&&task?.Quest!=null&&QuestOriginZone(task.Quest)==me.GetZoneId()&&
            me.GetQuestStatus(task.Quest.Id) is QuestStatus.Incomplete or QuestStatus.Complete)
            _authorizedQuestZoneTrips.Add(task.Quest.Id);
    }
    static bool CollectsCloth(Quest quest)=>quest?.Objectives?.Any(o=>o.Type==QuestObjectiveType.Item&&
        IsQuestCloth(o.ObjectID))==true;
    static bool IsQuestCloth(int entry)
    {
        if(entry is 2589 or 2592 or 4306 or 4338 or 14047 or 21877 or 33470)return true;
        var item=Global.ObjectMgr.GetItemTemplate(entry);
        return item!=null&&(item.GetClass()==ItemClass.TradeGoods&&item.GetSubClass().data==(int)ItemSubClassTradeGoods.Cloth||
            item.GetClass()==ItemClass.Quest&&item.GetName().Contains("cloth",StringComparison.OrdinalIgnoreCase));
    }

    SITask _priorityTurnInTask,_turnInReturnTask;
    long _nextPriorityTurnInScan;
    const float EngagedTurnInDetour=80;
    void ResetPriorityTurnIn(){_priorityTurnInTask=null;_turnInReturnTask=null;_nextPriorityTurnInScan=0;}
    bool TickPriorityTurnIn()
    {
        if(!WorkingGoal||!SIQuestKnowledge.IsBuilt||!_preparation.Complete||me.IsInCombat()||_encounter.Active||_recovering||_resting||!_gatherTarget.IsEmpty()||
            me.IsNonMeleeSpellCast(false,skipAutorepeat:true))return false;
        if(_priorityTurnInTask!=null)
        {
            if(!ReferenceEquals(_task,_priorityTurnInTask)||TaskDone(_priorityTurnInTask)||Blacklisted(_priorityTurnInTask.Key))
            {
                var resume=_turnInReturnTask;ResetPriorityTurnIn();
                SetTask(resume!=null&&!TaskDone(resume)&&!Blacklisted(resume.Key)?resume:null);
                _nextReplan=0;return true;
            }
            return RunPriorityTurnIn();
        }
        if(Now<_nextPriorityTurnInScan)return false;
        _nextPriorityTurnInScan=Now+1000;
        // Only givers in the bot's own area: a finished quest is never worth a trip back on its own. Distant
        // turn-ins wait for the planner, which goes there when the giver is the nearest work left. While an
        // objective is engaged, only a giver close beside the bot interrupts it.
        float local=CommittedToObjective()?EngagedTurnInDetour:_settings.TargetSearchRadius;
        SITask best=null;float closest=float.MaxValue;
        foreach(var (_,quest) in QuestLog())
        {
            if(me.GetQuestStatus(quest.Id)!=QuestStatus.Complete||CollectsCloth(quest))continue;
            foreach(var spawn in Knowledge.Spawns(Knowledge.CreatureEnders(quest.Id),Knowledge.ObjectEnders(quest.Id),me.GetMapId()))
            {
                float distance=Distance(spawn.Position);
                if(distance>=closest||!SpawnAvailable(spawn)||!SocialServiceAllowed(spawn)||!spawn.GameObject&&!ServiceSpawnVisible(spawn)||
                    !spawn.GameObject&&!FriendlyTemplate(spawn.Entry)||IsLearnedRouteBlocked(P(spawn.Position)))continue;
                if(distance>local)continue;
                var candidate=new SITask{Kind=SITaskKind.TurnIn,Quest=quest,Destination=spawn,
                    Creatures=spawn.GameObject?[]:[spawn.Entry],Objects=spawn.GameObject?[spawn.Entry]:[],Label="Turn in \""+quest.LogTitle+"\""};
                if(Blacklisted(candidate.Key))continue;
                best=candidate;closest=distance;
            }
        }
        if(best==null)return false;
        if(_task?.Kind==SITaskKind.TurnIn&&_task.Key==best.Key)
        {
            _priorityTurnInTask=_task;_turnInReturnTask=null;
            return RunPriorityTurnIn();
        }
        _turnInReturnTask=_task!=null&&!TaskDone(_task)?_task:null;
        SetTask(best);_priorityTurnInTask=best;
        return RunPriorityTurnIn();
    }
    bool RunPriorityTurnIn()
    {
        if(Recuperate()||Rest()||Survive()){Behavior="Recovery";_task?.Suspend(Now);return true;}
        Behavior="TurnIn";_task.Resume(Now);VisitNpc(_task);return true;
    }
}
