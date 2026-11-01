using Framework.Configuration;
using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using System;
using System.Globalization;

namespace Game.Systems.PvP
{
    // Classic 1.60 (WoW Forever) PvP ranks, built from the client's own data:
    //   Rank Points (currency 3468) convert into reputation with "PvP Rank Points" (faction 2800).
    //   That faction is a renown track: its renown currency 3473 ("Renown - PvP Rank", max 14) is the rank, and
    //   RenownThresholdCurveID 103650 gives the points each rank takes (750 for rank 1 ... 3000 for rank 14).
    //   Currency 3473's MaxQtyCurveID 102601 is the weekly cap: week of the season -> highest rank (3, 5, 6 ... 14 from week 10).
    // Rank Points come from battlegrounds and from the Field of Honor journey (quests 96912-96921, one per season week).
    // Blizzard has not published how many Rank Points a battleground gives; those amounts are configuration.
    public static class ForeverPvpRanks
    {
        public const int RankPointsCurrencyId = 3468;
        public const int RankFactionId = 2800;
        public const int RankCurrencyId = 3473;
        public const int JourneyFirstQuestId = 96912;   // week n = 96911 + n (Legacy challenges: week 4 = 96915, 7 = 96918, 10 = 96921)
        public const int JourneyWeeks = 10;
        public const int MaxRank = 14;
        static readonly int[] CallToArmsRotation = [2997, 529, 489]; // Darkspear Islands, Arathi Basin, Warsong Gulch

        static DateTime _seasonStart;
        public static bool Enabled { get; private set; }
        public static int BattlegroundWinPoints { get; private set; }
        public static int BattlegroundLossPoints { get; private set; }
        public static int CallToArmsBonusPct { get; private set; }

        public static void LoadConfig()
        {
            Enabled = ConfigMgr.GetDefaultValue("PvP.Ranks.Enabled", true);
            string start = ConfigMgr.GetDefaultValue("PvP.Season.Start", "2026-11-04");
            if (!DateTime.TryParse(start, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out _seasonStart))
            {
                Log.outError(LogFilter.ServerLoading, $"PvP.Season.Start '{start}' is not a date; using 2026-11-04.");
                _seasonStart = new DateTime(2026, 11, 4);
            }
            BattlegroundWinPoints = Math.Max(0, ConfigMgr.GetDefaultValue("PvP.RankPoints.BattlegroundWin", 150));
            BattlegroundLossPoints = Math.Max(0, ConfigMgr.GetDefaultValue("PvP.RankPoints.BattlegroundLoss", 60));
            CallToArmsBonusPct = Math.Max(0, ConfigMgr.GetDefaultValue("PvP.RankPoints.CallToArmsBonusPct", 50));
        }

        // 1 in the season's first week, 0 before the season starts.
        public static int SeasonWeek()
        {
            DateTime now = DateTime.Now;
            if (!Enabled || now < _seasonStart) return 0;
            return (int)((now - _seasonStart).TotalDays / 7) + 1;
        }

        // Highest rank reachable this week (client curve 102601).
        public static int RankCap(CurrencyTypesRecord rankCurrency)
        {
            int week = SeasonWeek();
            if (week <= 0) return 0;
            return Math.Min(rankCurrency.MaxQty, (int)Global.DB2Mgr.GetCurveValueAt(rankCurrency.MaxQtyCurveID, week));
        }

        // Points needed to go from 'level' to the next renown level on a curve-based track (PvP ranks: 750 ... 3000).
        public static int RenownThreshold(FactionRecord faction, int level) =>
            Math.Max(1, (int)Global.DB2Mgr.GetCurveValueAt(faction.RenownThresholdCurveID, level + 1));

        // This week's Call to Arms battleground, in turn: Darkspear Islands, Arathi Basin, Warsong Gulch.
        public static int CallToArmsMapId()
        {
            int week = SeasonWeek();
            return week <= 0 ? 0 : CallToArmsRotation[(week - 1) % CallToArmsRotation.Length];
        }

        public static void AwardBattleground(Player player, bool won, int mapId)
        {
            if (!Enabled || SeasonWeek() <= 0) return;
            int points = won ? BattlegroundWinPoints : BattlegroundLossPoints;
            if (mapId == CallToArmsMapId())
                points += points * CallToArmsBonusPct / 100;
            if (points > 0)
                player.ModifyCurrency(RankPointsCurrencyId, points, CurrencyGainSource.PvPCompletionBonus);
        }

        // Field of Honor: week n's quest opens in week n of the season and stays open afterwards.
        public static bool IsJourneyQuest(int questId) => questId >= JourneyFirstQuestId && questId < JourneyFirstQuestId + JourneyWeeks;
        public static bool JourneyQuestOpen(int questId) => !IsJourneyQuest(questId) || SeasonWeek() >= questId - JourneyFirstQuestId + 1;
    }
}
