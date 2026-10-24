using Game.Entities;
using System.Collections.Generic;
using System.Linq;
namespace Game.AI;
public sealed partial class SIPlayer
{
    readonly Dictionary<int,long> _acceptedQuestOrder=new();
    long _nextAcceptedQuestOrder;
    void SyncQuestAcceptance()
    {
        var log=QuestLog().ToArray();var active=log.Select(q=>q.Quest.Id).ToHashSet();
        foreach(var id in _acceptedQuestOrder.Keys.Where(id=>!active.Contains(id)).ToArray())_acceptedQuestOrder.Remove(id);
        foreach(var item in log.OrderBy(q=>me.GetQuestAcceptTime(q.Quest.Id)).ThenBy(q=>q.Slot))
            if(!_acceptedQuestOrder.ContainsKey(item.Quest.Id))_acceptedQuestOrder[item.Quest.Id]=++_nextAcceptedQuestOrder;
    }
    void RecordQuestAcceptance(Quest quest){SyncQuestAcceptance();}
    long QuestAcceptanceOrder(Quest quest)
    {if(!_acceptedQuestOrder.ContainsKey(quest.Id))SyncQuestAcceptance();return _acceptedQuestOrder.GetValueOrDefault(quest.Id,long.MaxValue);}
}
