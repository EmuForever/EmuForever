using Framework.Constants;
using Framework.Database;
using Framework.Configuration;
using Game.DataStorage;
using Game.Entities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Game.Systems.Legacy
{
    // Legacy uses the client's generic trait system, achievement criteria and renown reward quests.
    // Do not introduce a second perk inventory or a second wire protocol.
    public static class LegacyManager
    {
        public const int TraitSystemId = 45;
        public const int PointsCurrencyId = 4225;
        public const int RenownCurrencyId = 3485;
        public const int RewardFactionId = 2802;
        public static int RulesetId { get; private set; }
        public static int SpendingLimit { get; private set; }
        public static string RulesetName { get; private set; }
        public static IReadOnlyDictionary<int, TraitCurrencySourceRecord> Challenges => _challenges;
        static Dictionary<int, TraitCurrencySourceRecord> _challenges = new();
        static readonly ConcurrentDictionary<(byte, uint, int), LegacyAccountProgress> Accounts = new();
        static HashSet<int> _allChallenges = new();
        public static bool IsForeignChallenge(int achievementId) => _allChallenges.Contains(achievementId) && !_challenges.ContainsKey(achievementId);
        public static (byte Kind, uint Id) AccountKey(Player player)
        {
            int bnet = player.GetSession().GetBattlenetAccountId();
            return bnet != 0 ? ((byte)1, unchecked((uint)bnet)) : ((byte)0, unchecked((uint)player.GetSession().GetAccountId()));
        }

        public static bool IsLegacy(Game.Networking.Packets.TraitConfigPacket config) =>
            config.Type == TraitConfigType.Generic && config.TraitSystemID == TraitSystemId;

        public static void Load()
        {
            // The extracted client has two sets of 65 sources, 60 and 61. Ruleset 0 has none.
            // Select the realm's verified client ruleset explicitly; never combine them.
            RulesetId = ConfigMgr.GetDefaultValue("Legacy.SuperDistrictSetID", 60);
            if (CliDB.AvailableSuperDistrictStorage.LayoutHash != 0x87BD1F4D || CliDB.SuperDistrictSetXAvailableSDStorage.LayoutHash != 0x19A3AA0B)
                throw new InvalidOperationException("Legacy client ruleset DB2 layouts have not been verified.");
            var districts = CliDB.SuperDistrictSetXAvailableSDStorage.Values.Where(x => x.SuperDistrictSetID == RulesetId).ToArray();
            if (districts.Length == 0 || districts.Any(x => !CliDB.AvailableSuperDistrictStorage.ContainsKey(x.AvailableSuperDistrictID)))
                throw new InvalidOperationException("Legacy ruleset has no valid client SuperDistrict relationships.");
            RulesetName = string.Join("/", districts.Select(x => CliDB.AvailableSuperDistrictStorage[x.AvailableSuperDistrictID].DisplayName[Locale.enUS]));
            SpendingLimit = CliDB.TraitCurrencyStorage.LookupByKey(PointsCurrencyId)?.SourcedMax ?? 0;
            _allChallenges = CliDB.TraitCurrencySourceStorage.Values.Where(x => x.TraitCurrencyID == PointsCurrencyId && x.AchievementID != 0).Select(x => x.AchievementID).ToHashSet();
            _challenges = CliDB.TraitCurrencySourceStorage.Values
                .Where(x => x.TraitCurrencyID == PointsCurrencyId && x.SuperDistrictSetID == RulesetId && x.AchievementID != 0)
                .GroupBy(x => x.AchievementID).ToDictionary(x => x.Key, x => x.First());
            int invalidAmounts = _challenges.Values.Count(x => x.Amount <= 0);
            int[] missingAchievements = _challenges.Keys.Where(x => !CliDB.AchievementStorage.ContainsKey(x)).ToArray();
            if (SpendingLimit <= 0 || _challenges.Count == 0 || invalidAmounts != 0 || missingAchievements.Length != 0)
            {
                string sourceSets = string.Join(", ", CliDB.TraitCurrencySourceStorage.Values
                    .Where(x => x.TraitCurrencyID == PointsCurrencyId).GroupBy(x => x.SuperDistrictSetID)
                    .Select(x => $"{x.Key}:{x.Count()}"));
                throw new InvalidOperationException($"Legacy client data/ruleset is incomplete: ruleset={RulesetId} ({RulesetName}), " +
                    $"currency={PointsCurrencyId}, spendingLimit={SpendingLimit}, challenges={_challenges.Count}, invalidAmounts={invalidAmounts}, " +
                    $"missingAchievements=[{string.Join(", ", missingAchievements.Take(10))}], loadedSourceSets=[{sourceSets}]. " +
                    "Check Legacy.SuperDistrictSetID, extracted DB2 files and hotfix overrides.");
            }
            LegacyRewardManager.Load();
            LegacyDefinitions.Load();
            Log.outInfo(LogFilter.ServerLoading, $"Legacy: {_challenges.Count} challenges, {SpendingLimit} spendable points, client ruleset {RulesetId} ({RulesetName}).");
        }

        public static LegacyAccountProgress LoadAccount(Player player)
        {
            var account = AccountKey(player);
            var progress = Accounts.GetOrAdd((account.Kind, account.Id, RulesetId), _ => new LegacyAccountProgress());
            PreparedStatement stmt = LoginDatabase.GetPreparedStatement(LoginStatements.SEL_LEGACY_CHALLENGES);
            stmt.SetUInt8(0, account.Kind); stmt.SetUInt32(1, account.Id);
            stmt.SetInt32(2, RulesetId);
            SQLResult result = DB.Login.Query(stmt);
            if (!result.IsEmpty())
                do
                {
                    int id = result.Read<int>(0);
                    if (_challenges.ContainsKey(id)) progress.Add(id, result.Read<long>(1));
                } while (result.NextRow());

            // Match the existing collection system's Battle.net scope; standalone SI accounts remain isolated.
            List<uint> licenses = new() { unchecked((uint)player.GetSession().GetAccountId()) };
            if (account.Kind == 1)
            {
                stmt = LoginDatabase.GetPreparedStatement(LoginStatements.SEL_LEGACY_WOW_ACCOUNTS);
                stmt.SetUInt32(0, account.Id);
                result = DB.Login.Query(stmt);
                if (!result.IsEmpty()) do { uint id = result.Read<uint>(0); if (!licenses.Contains(id)) licenses.Add(id); } while (result.NextRow());
            }
            SQLTransaction migration = new();
            List<(int Id, long Date)> imported = new();
            HashSet<int> known = progress.Snapshot().Select(x => x.Key).ToHashSet();
            foreach (uint license in licenses)
            {
                stmt = CharacterDatabase.GetPreparedStatement(CharStatements.SEL_LEGACY_ACHIEVEMENT_HISTORY);
                stmt.SetUInt32(0, license);
                result = DB.Characters.Query(stmt);
                if (!result.IsEmpty())
                    do
                    {
                        int id = result.Read<int>(0);
                        if (!_challenges.ContainsKey(id) || !known.Add(id)) continue;
                        long date = result.Read<long>(1);
                        AppendCompletion(migration, account, id, date, result.Read<long>(2));
                        imported.Add((id, date));
                    } while (result.NextRow());
            }
            if (migration.GetSize() != 0)
            {
                if ((int)DB.Login.DirectCommitTransaction(migration) != 0)
                    throw new InvalidOperationException("Legacy account achievement migration failed; refusing to validate perks against incomplete account points.");
                foreach (var item in imported) progress.Add(item.Id, item.Date);
            }
            return progress;
        }

        public static void ValidatePersistence()
        {
            void Require(Func<string,SQLResult> query, string table, string column)
            {
                SQLResult result = query($"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = '{table}' AND column_name = '{column}'");
                if (result.IsEmpty() || result.Read<long>(0) != 1)
                    throw new InvalidOperationException($"Legacy persistence is missing {table}.{column}; apply the 2026_10_19_00_legacy auth and characters migrations before starting the server.");
            }
            Require(sql => DB.Login.Query(sql), "legacy_account_challenge", "account_kind");
            Require(sql => DB.Login.Query(sql), "legacy_account_reward", "account_kind");
            Require(sql => DB.Characters.Query(sql), "character_legacy_state", "revision");
        }

        public static void CompleteChallenge(Player player, int achievementId)
        {
            if (!_challenges.ContainsKey(achievementId) || player.LegacyProgress == null) return;
            var account = AccountKey(player);
            long date = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            SQLTransaction transaction = new();
            AppendCompletion(transaction, account, achievementId, date, player.GetGUID().GetCounter());
            LegacyAccountProgress progress = player.LegacyProgress;
            player.GetSession().AddTransactionCallback(DB.Login.AsyncCommitTransaction(transaction)).AfterComplete(success =>
            {
                if (!success)
                {
                    Log.outError(LogFilter.Achievement, $"Legacy challenge {achievementId} persistence failed for account {account}; character achievement remains available for retry on login.");
                    return;
                }
                progress.Add(achievementId, date);
                Log.outInfo(LogFilter.Achievement, $"Legacy account {account}, ruleset {RulesetId}: challenge {achievementId} completed.");
            });
        }

        static void AppendCompletion(SQLTransaction transaction, (byte Kind, uint Id) account, int id, long date, long guid)
        {
            PreparedStatement stmt = LoginDatabase.GetPreparedStatement(LoginStatements.INS_LEGACY_CHALLENGE);
            stmt.SetUInt8(0, account.Kind); stmt.SetUInt32(1, account.Id);
            stmt.SetInt32(2, RulesetId);
            stmt.SetInt32(3, id);
            stmt.SetInt64(4, date);
            stmt.SetInt64(5, guid);
            transaction.Append(stmt);
        }

        public static int EarnedPoints(IEnumerable<int> completed) => completed.Distinct()
            .Where(_challenges.ContainsKey).Sum(id => _challenges[id].Amount);
    }

    public sealed class LegacyAccountProgress
    {
        readonly object _gate = new();
        readonly Dictionary<int, long> _completed = new();
        int _version;
        // Read by every player update; int reads are atomic, writes still happen under _gate.
        public int Version => System.Threading.Volatile.Read(ref _version);
        public KeyValuePair<int, long>[] Snapshot() { lock (_gate) return _completed.ToArray(); }
        public int Points => LegacyManager.EarnedPoints(Snapshot().Select(x => x.Key));
        public void Add(int id, long date)
        {
            lock (_gate)
                if (_completed.TryAdd(id, date)) ++_version;
        }
    }
}
