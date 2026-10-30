using Framework.Constants;
using Framework.Database;
using Game.DataStorage;
using Game.Quests;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game;

public sealed partial class ObjectManager
{
    Dictionary<int, int[]> _uiMapQuestLinesStore = new();
    Dictionary<int, int[]> _uiMapQuestsStore = new();

    public IReadOnlyList<int> GetUiMapQuestLinesList(int map) =>
        _uiMapQuestLinesStore.TryGetValue(map, out var lines) ? lines : Array.Empty<int>();
    public IReadOnlyList<int> GetUiMapQuestsList(int map) =>
        _uiMapQuestsStore.TryGetValue(map, out var quests) ? quests : Array.Empty<int>();

    public void LoadUiMapQuestLines() => _uiMapQuestLinesStore = LoadUiMapQuestMappings("ui_map_quest_line", "QuestLineId",
        id => Global.DB2Mgr.GetQuestsForQuestLine(id).Count != 0);
    public void LoadUiMapQuests() => _uiMapQuestsStore = LoadUiMapQuestMappings("ui_map_quest", "QuestId",
        id => GetQuestTemplate(id) != null);

    static Dictionary<int, int[]> LoadUiMapQuestMappings(string table, string field, Func<int, bool> validEntry)
    {
        RelativeTime started = Time.NowRelative;
        var rows = new List<(int, int)>();
        SQLResult result = DB.World.Query($"SELECT UiMapId, {field} FROM {table}");
        if (!result.IsEmpty())
            do { rows.Add((result.Read<int>(0), result.Read<int>(1))); } while (result.NextRow());
        int rejected = 0;
        var examples = new List<string>();
        var index = QuestMapAvailability.Index(rows, id => CliDB.UiMapStorage.ContainsKey(id), validEntry,
            (map, entry) =>
            {
                ++rejected;
                if (examples.Count < 3) examples.Add($"UIMap {map}/{field} {entry}");
            });
        if (rejected != 0)
            Log.outWarn(LogFilter.Sql, $"Table `{table}`: skipped {rejected} mappings absent from the loaded client/quest data ({string.Join(", ", examples)}). Maps without compatible mappings return empty quest lists.");
        Log.outInfo(LogFilter.ServerLoading, $"Loaded {index.Values.Sum(ids => ids.Length)} UI map quest mappings from `{table}` in {Time.Diff(started)} ms.");
        return index;
    }
}
