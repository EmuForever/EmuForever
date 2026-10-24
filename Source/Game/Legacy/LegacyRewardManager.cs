using Framework.Constants;
using Framework.Database;
using Game.DataStorage;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Systems.Legacy
{
    public static class LegacyRewardManager
    {
        static Dictionary<int, RenownRewardsRecord> _rewards = new();
        public static IReadOnlyDictionary<int, RenownRewardsRecord> Rewards => _rewards;
        public static void Load()
        {
            if (CliDB.RenownRewardsStorage.LayoutHash != 0x30AFBCF8)
                throw new InvalidOperationException("Legacy RenownRewards DB2 layout has not been verified for this client.");
            _rewards = CliDB.RenownRewardsStorage.Values.Where(x => x.CovenantID == 48 && x.ItemID != 0).ToDictionary(x => x.Id);
            if (_rewards.Count != 4 || _rewards.Values.Any(x => x.Level <= 0))
                throw new InvalidOperationException("Legacy reward DB2 relationships are incomplete.");
        }
        public static RenownRewardsRecord ForQuest(int questId)
        {
            // These four quests are the delivery quests in the supplied Forever reference/world database.
            // Match their actual item to the DB2 reward; levels, cosmetics and conditions remain client sourced.
            if (questId < 96339 || questId > 96342) return null;
            Quest quest = Global.ObjectMgr.GetQuestTemplate(questId);
            return quest == null ? null : _rewards.Values.FirstOrDefault(reward => quest.RewardItemId.Contains(reward.ItemID));
        }
        public static bool CanClaim(Player player, int questId)
        {
            RenownRewardsRecord reward = ForQuest(questId);
            if (questId is >= 96339 and <= 96342 && reward == null) return false;
            return reward == null || (player.GetLegacyPoints() >= reward.Level &&
                (reward.PlayerConditionID == 0 || ConditionManager.IsPlayerMeetingCondition(player, CliDB.PlayerConditionStorage.LookupByKey(reward.PlayerConditionID))));
        }
        public static void AppendState(SQLTransaction transaction, Player player)
        {
            foreach (RenownRewardsRecord reward in _rewards.Values)
            {
                if (player.GetLegacyPoints() < reward.Level) continue;
                int questId = Enumerable.Range(96339, 4).FirstOrDefault(id => ForQuest(id)?.Id == reward.Id);
                PreparedStatement stmt = LoginDatabase.GetPreparedStatement(LoginStatements.UPSERT_LEGACY_REWARD);
                var account = LegacyManager.AccountKey(player);
                stmt.SetUInt8(0, account.Kind); stmt.SetUInt32(1, account.Id);
                stmt.SetInt32(2, LegacyManager.RulesetId); stmt.SetInt32(3, reward.Id);
                if (questId != 0 && player.IsQuestRewarded(questId)) stmt.SetInt64(4, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); else stmt.SetNull(4);
                transaction.Append(stmt);
            }
        }
    }
}
