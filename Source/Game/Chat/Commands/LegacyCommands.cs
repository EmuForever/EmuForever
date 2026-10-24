using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Systems.Legacy;
using System.Linq;

namespace Game.Chat.Commands
{
    [CommandGroup("legacy")]
    class LegacyCommands
    {
        [Command("info", RBACPermissions.CommandAchievementAdd)]
        static bool Info(CommandHandler handler)
        {
            Player player = handler.GetSelectedPlayerOrSelf();
            if (player == null) return false;
            var config = player.GetLegacyConfig();
            int spent = config == null ? 0 : new Game.Networking.Packets.TraitConfigPacket(config).Entries.Sum(x => x.Rank);
            handler.SendSysMessage($"Legacy {player.GetName()}: ruleset {LegacyManager.RulesetId}, {player.GetLegacyPoints()} earned, {spent} ranks purchased, {LegacyManager.SpendingLimit} point limit, config {(config == null ? 0 : (int)config.ID)}.");
            return true;
        }

        [Command("complete", RBACPermissions.CommandAchievementAdd)]
        static bool Complete(CommandHandler handler, int achievementId)
        {
            Player player = handler.GetSelectedPlayerOrSelf();
            if (player == null || !LegacyManager.Challenges.ContainsKey(achievementId)) return false;
            player.CompletedAchievement(CliDB.AchievementStorage[achievementId]);
            handler.SendSysMessage("Challenge submitted through the achievement system; account sharing follows a successful database commit. GM mode and achievement permissions still apply.");
            return true;
        }

        [Command("reset", RBACPermissions.CommandAchievementAdd)]
        static bool Reset(CommandHandler handler)
        {
            Player player = handler.GetSelectedPlayerOrSelf();
            if (player == null || !player.ResetLegacyTraits()) return false;
            handler.SendSysMessage("Legacy reset submitted; it becomes active after the database commit.");
            return true;
        }
    }
}
