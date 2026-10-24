using Game.DataStorage;

namespace Game.Systems.Legacy
{
    public static class LegacyClientData
    {
        public static bool RestoreMissingRuleset(TraitCurrencySourceRecord original, TraitCurrencySourceRecord hotfix)
        {
            // Older hotfix imports omit the DB2 relationship field and supply its SQL default, 0.
            // Keep the matching client's parent, without replacing explicit hotfix relationships
            // or borrowing a ruleset for new/different challenges. All other hotfix fields survive.
            if (original.Id != hotfix.Id || original.TraitCurrencyID != LegacyManager.PointsCurrencyId ||
                hotfix.TraitCurrencyID != original.TraitCurrencyID || original.AchievementID == 0 ||
                hotfix.AchievementID != original.AchievementID || original.SuperDistrictSetID <= 0 ||
                hotfix.SuperDistrictSetID != 0)
                return false;

            hotfix.SuperDistrictSetID = original.SuperDistrictSetID;
            return true;
        }
    }
}
