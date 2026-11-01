using Game.DataStorage;
using Game.Systems.PvP;
using System;
using System.Collections.Generic;

namespace Game.Entities
{
    public partial class Player
    {
        int _pvpRankWeekSeen = -1;

        // Once per season week (and at login): apply Rank Points the previous week's cap held back.
        void UpdatePvpRank()
        {
            int week = ForeverPvpRanks.SeasonWeek();
            if (week == _pvpRankWeekSeen)
                return;
            _pvpRankWeekSeen = week;
            GetReputationMgr().RefreshCurveRenown(CliDB.FactionStorage.LookupByKey(ForeverPvpRanks.RankFactionId));
            UpdatePvpRankFields();
        }

        // The Classic rank fields: PvpRank uses the original numbering (ranks 1-14 are 5-18, 0 = none), PvpRankProgress is the
        // progress towards the next rank in 1/255ths.
        internal void UpdatePvpRankFields()
        {
            int rank = GetCurrencyQuantity(ForeverPvpRanks.RankCurrencyId);
            SetUpdateFieldValue(m_values.ModifyValue(m_playerData).ModifyValue(m_playerData.PvpRank), (byte)(rank > 0 ? rank + 4 : 0));

            FactionRecord faction = CliDB.FactionStorage.LookupByKey(ForeverPvpRanks.RankFactionId);
            int progress = 0;
            if (faction != null && rank < ForeverPvpRanks.MaxRank)
                progress = Math.Clamp(GetReputationMgr().GetReputation(faction) * 255 / ForeverPvpRanks.RenownThreshold(faction, rank), 0, 255);
            SetUpdateFieldValue(m_values.ModifyValue(m_activePlayerData).ModifyValue(m_activePlayerData.PvpRankProgress), (byte)progress);
        }
    }
}
