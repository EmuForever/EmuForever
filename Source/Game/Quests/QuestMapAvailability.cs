using Game.DataStorage;
using Game.Networking.Packets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Quests;

public readonly record struct QuestMapQuestState(bool Exists, bool Available, bool Rewarded);

public static class QuestMapAvailability
{
    // The wire list contains QuestLineXQuest record IDs, not the underlying quest
    // IDs. A line remains visible until all of its existing quests are rewarded.
    public static UiMapQuestLinesResponse Build(int uiMapId, IEnumerable<int> lines, IEnumerable<int> quests,
        Func<int, IReadOnlyList<QuestLineXQuestRecord>> lineQuests, Func<int, QuestMapQuestState> state)
    {
        var response = new UiMapQuestLinesResponse { UiMapID = uiMapId };
        foreach (int line in lines.Distinct())
        {
            var entries = lineQuests(line);
            if (entries == null || entries.Count == 0) continue;
            bool completed = true;
            foreach (var entry in entries.OrderBy(q => q.OrderIndex).ThenBy(q => q.Id))
            {
                var quest = state(entry.QuestID);
                if (!quest.Exists) continue;
                if (quest.Available) response.QuestLineXQuestIDs.Add((uint)entry.Id);
                if (!quest.Rewarded) completed = false;
            }
            if (!completed) response.QuestLineIDs.Add((uint)line);
        }
        foreach (int id in quests.Distinct())
        {
            var quest = state(id);
            if (quest.Exists && quest.Available) response.QuestIDs.Add((uint)id);
        }
        return response;
    }

    public static Dictionary<int, int[]> Index(IEnumerable<(int UiMap, int Entry)> rows,
        Func<int, bool> validMap, Func<int, bool> validEntry, Action<int, int> invalid = null)
    {
        var maps = new Dictionary<int, HashSet<int>>();
        foreach (var (map, entry) in rows)
        {
            if (!validMap(map) || !validEntry(entry)) { invalid?.Invoke(map, entry); continue; }
            if (!maps.TryGetValue(map, out var list)) maps[map] = list = new();
            list.Add(entry);
        }
        return maps.ToDictionary(pair => pair.Key, pair => pair.Value.OrderBy(id => id).ToArray());
    }
}
