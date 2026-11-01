using Game.DataStorage;
using System.Collections.Generic;

namespace Game.Systems.Legacy
{
    public static class LegacyClientData
    {
        // Ruleset (SuperDistrictSetID) of each Legacy challenge as the client file has it, kept for the server's own checks.
        static readonly Dictionary<int, int> _fileRulesets = new();

        public static int RulesetOf(TraitCurrencySourceRecord source) =>
            _fileRulesets.TryGetValue(source.Id, out int ruleset) ? ruleset : source.SuperDistrictSetID;

        // The hotfix rows of the Legacy challenges carry SuperDistrictSetID 0, and the record stays that way because it is what
        // clients receive: the Legacy trees only count a trait-sourced point whose ruleset matches the one the client believes it
        // is on, so with the file's 60 a realm the client maps elsewhere shows "Available points: 0" while the Legacy tab still
        // counts the completed challenges. The server keeps using the file's ruleset through RulesetOf.
        public static bool RememberFileRuleset(TraitCurrencySourceRecord original, TraitCurrencySourceRecord hotfix)
        {
            if (original.Id != hotfix.Id || original.TraitCurrencyID != LegacyManager.PointsCurrencyId ||
                hotfix.TraitCurrencyID != original.TraitCurrencyID || original.AchievementID == 0 ||
                hotfix.AchievementID != original.AchievementID || original.SuperDistrictSetID <= 0 ||
                hotfix.SuperDistrictSetID != 0)
                return false;

            _fileRulesets[original.Id] = original.SuperDistrictSetID;
            return true;
        }
    }
}
