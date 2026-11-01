using Framework.Constants;
using Game.BattleGrounds;
using System.Linq;

namespace Game.Chat.Commands;

[CommandGroup("bg")]
class BattlegroundCommands
{
    [Command("list", RBACPermissions.RoleModerator, true)]
    static bool List(CommandHandler handler)
    {
        var matches = Global.BattlegroundMgr.GetActiveBattlegrounds();
        if (matches.Length == 0) { handler.SendSysMessage("No active battlegrounds."); return true; }
        foreach (var bg in matches)
            handler.SendSysMessage($"{bg.GetName()} | instance {bg.GetInstanceID()} | map {bg.GetMapId()} | levels {bg.GetMinLevel()}-{bg.GetMaxLevel()} | " +
                $"{Status(bg.GetStatus())} | players {bg.GetPlayers().Count} (Alliance {bg.GetPlayersCountByTeam(Team.Alliance)}, Horde {bg.GetPlayersCountByTeam(Team.Horde)}) | elapsed {bg.GetElapsedTime().TotalMinutes:F1} min");
        handler.SendSysMessage($"Active battlegrounds: {matches.Length}. Use .bg stats for queue totals.");
        return true;
    }

    [Command("stats", RBACPermissions.RoleModerator, true)]
    static bool Stats(CommandHandler handler)
    {
        var matches = Global.BattlegroundMgr.GetActiveBattlegrounds();
        var queue = Global.BattlegroundMgr.GetBattlegroundQueueStatistics();
        handler.SendSysMessage($"Battlegrounds: {matches.Length} active ({matches.Count(bg => bg.GetStatus() == BattlegroundStatus.WaitJoin)} preparing, " +
            $"{matches.Count(bg => bg.GetStatus() == BattlegroundStatus.InProgress)} running, {matches.Count(bg => bg.GetStatus() == BattlegroundStatus.WaitLeave)} finished).");
        handler.SendSysMessage($"Participants: {matches.Sum(bg => bg.GetPlayers().Count)} (Alliance {matches.Sum(bg => bg.GetPlayersCountByTeam(Team.Alliance))}, " +
            $"Horde {matches.Sum(bg => bg.GetPlayersCountByTeam(Team.Horde))}).");
        handler.SendSysMessage($"Queued players: {queue.Players} unique (Alliance {queue.Alliance}, Horde {queue.Horde}); " +
            $"{queue.Tickets} queue entries; {queue.Invited} invited. Players queued for several battlegrounds count once in the unique total.");
        handler.SendSysMessage($"Battleground testing mode: {(Global.BattlegroundMgr.IsTesting() ? "on" : "off")}. Arenas are excluded.");
        return true;
    }

    internal static string Status(BattlegroundStatus status) => status switch
    {
        BattlegroundStatus.WaitJoin => "Preparing", BattlegroundStatus.InProgress => "In progress",
        BattlegroundStatus.WaitLeave => "Finished", _ => status.ToString()
    };
}
