using Framework.Constants;
using Game.Entities;
using System;
using System.Linq;
namespace Game.AI;
public sealed partial class SIPlayer
{
    int _questGrindUntilLevel;
    long _nextLevelGapScan;
    int _levelGapScanLevel;
    bool QuestNeedsLevelGrinding()
    {
        if(!SIQuestKnowledge.IsBuilt)return false;
        if(Now<_nextLevelGapScan&&_levelGapScanLevel==me.GetLevel())return _questGrindUntilLevel>me.GetLevel();
        _nextLevelGapScan=Now+5000;_levelGapScanLevel=me.GetLevel();_questGrindUntilLevel=0;
        // Failed navigation, unsupported objectives or a deferred turn-in must not
        // turn a quest bot into an unrelated grinder.
        var log=QuestLog().Where(q=>me.GetQuestStatus(q.Quest.Id) is QuestStatus.Incomplete or QuestStatus.Complete).ToArray();
        if(log.Length!=0)
        {
            var first=log.OrderBy(q=>QuestAcceptanceOrder(q.Quest)).First().Quest;
            if(QuestLevelEligible(first))return false;
            if(Knowledge.Plan(first).Supported&&SIQuestPolicy.TooHigh(me.GetLevel(),QuestLevel(first),first.MinLevel))
                _questGrindUntilLevel=Math.Max(first.MinLevel,QuestLevel(first)-2);
            return _questGrindUntilLevel>me.GetLevel();
        }
        foreach(int id in Knowledge.QuestsStartedOnMap(me.GetMapId()))
        {
            var quest=Global.ObjectMgr.GetQuestTemplate(id);
            if(quest==null||_abandonedQuests.Contains(id)||me.GetQuestStatus(id)!=QuestStatus.None||
                !Knowledge.Plan(quest).Supported||!me.CanTakeQuestAfterLeveling(quest)||!me.SatisfyQuestExpansion(quest,false)||!me.CanAddQuest(quest,false))continue;
            var spawn=NearestSpawn(Knowledge.CreatureStarters(id),Knowledge.ObjectStarters(id),true);
            if(spawn==null||Distance(spawn.Value.Position)>QuestPickupRadius)continue;
            if(WantsQuest(quest)){_questGrindUntilLevel=0;return false;}
            if(!SIQuestPolicy.TooHigh(me.GetLevel(),QuestLevel(quest),quest.MinLevel))continue;
            int required=Math.Max(quest.MinLevel,QuestLevel(quest)-2);
            if(_questGrindUntilLevel==0||required<_questGrindUntilLevel)_questGrindUntilLevel=required;
        }
        return _questGrindUntilLevel>me.GetLevel();
    }
}
