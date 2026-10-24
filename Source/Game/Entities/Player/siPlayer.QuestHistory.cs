using Framework.Database;
using System;

namespace Game.Entities;

public sealed partial class siPlayer
{
    public long LifetimeQuestsCompleted { get; private set; }
    public long LifetimeQuestsFailed { get; private set; }
    public long LifetimeQuestsAbandoned { get; private set; }
    public long QuestHistorySince { get; private set; }
    bool _questHistoryLoaded;

    void LoadQuestHistory()
    {
        var row=DB.Characters.Query("SELECT completed,failed,abandoned,tracked_since FROM character_si_quest_history WHERE guid={0}",GetGUID().GetCounter());
        if(!row.IsEmpty())
        {
            LifetimeQuestsCompleted=row.Read<long>(0);LifetimeQuestsFailed=row.Read<long>(1);
            LifetimeQuestsAbandoned=row.Read<long>(2);QuestHistorySince=row.Read<long>(3);
        }
        else
        {
            // Rewarded IDs are the only durable pre-migration history. Repeat rewards,
            // failures and abandonments before this timestamp cannot be reconstructed.
            LifetimeQuestsCompleted=GetRewardedQuestCount();QuestHistorySince=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        _questHistoryLoaded=true;
    }
    internal void RecordQuestReward(){if(_questHistoryLoaded)++LifetimeQuestsCompleted;}
    internal void RecordQuestFailure(){if(_questHistoryLoaded)++LifetimeQuestsFailed;}
    internal void RecordQuestAbandonment(){if(_questHistoryLoaded)++LifetimeQuestsAbandoned;}
    internal void AppendQuestHistory(SQLTransaction transaction)
    {
        if(!_questHistoryLoaded)return;
        // Share the character-save transaction: a reward and its counter commit together.
        transaction.Append("INSERT INTO character_si_quest_history (guid,completed,failed,abandoned,tracked_since) VALUES ({0},{1},{2},{3},{4}) ON DUPLICATE KEY UPDATE completed=VALUES(completed),failed=VALUES(failed),abandoned=VALUES(abandoned)",
            GetGUID().GetCounter(),LifetimeQuestsCompleted,LifetimeQuestsFailed,LifetimeQuestsAbandoned,QuestHistorySince);
    }
}
