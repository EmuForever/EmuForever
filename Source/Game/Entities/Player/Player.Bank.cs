using Framework.Constants;
using Framework.Database;
using Game.DataStorage;
using Game.Networking.Packets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Entities
{
    public partial class Player
    {
        public const int ClassicBankTabBagItem = 242709;
        public const int MaxCharacterBankTabs = InventorySlots.BankBagEnd - InventorySlots.BankBagStart;
        bool _classicBankSettingsChanged;

        public byte GetCharacterBankTabCount() => m_activePlayerData.NumCharacterBankTabs;

        public void SetCharacterBankTabCount(byte count)
        {
            count = (byte)Math.Min((int)count, MaxCharacterBankTabs);
            SetUpdateFieldValue(m_values.ModifyValue(m_activePlayerData).ModifyValue(m_activePlayerData.NumCharacterBankTabs), count);
            while (m_activePlayerData.CharacterBankTabSettings.Size() < count)
            {
                int tab = m_activePlayerData.CharacterBankTabSettings.Size();
                SetCharacterBankTabSettings(tab, $"Tab {tab + 1}", "", "", BagSlotFlags.None);
            }
        }

        public void SetCharacterBankTabSettings(int tab, string name, string icon, string description, BagSlotFlags flags)
        {
            if (tab < 0 || tab >= MaxCharacterBankTabs)
                return;
            while (m_activePlayerData.CharacterBankTabSettings.Size() <= tab)
                AddDynamicUpdateFieldValue(m_values.ModifyValue(m_activePlayerData).ModifyValue(m_activePlayerData.CharacterBankTabSettings), new BankTabSettings());
            BankTabSettings settings = m_values.ModifyValue(m_activePlayerData).ModifyValue(m_activePlayerData.CharacterBankTabSettings, tab);
            settings.ModifyValue(settings.Name).SetValue(name ?? "");
            settings.ModifyValue(settings.Icon).SetValue(icon ?? "");
            settings.ModifyValue(settings.Description).SetValue(description ?? "");
            settings.ModifyValue(settings.DepositFlags).SetValue((int)flags);
            _classicBankSettingsChanged = true;
            AddToObjectUpdateIfNeeded();
        }

        // The first tab is the real bank container. Later tabs unlock slots for player-owned bags.
        public bool GrantClassicFreeBankTab()
        {
            Item existing = GetItemByPos(InventorySlots.BankBagStart);
            if (existing == null)
            {
                Item bag = Item.CreateItem(ClassicBankTabBagItem, 1, ItemContext.None, this);
                if (bag == null)
                    return false;
                BankItem(new List<ItemPosCount> { new(new ItemPos(InventorySlots.BankBagStart), 1) }, bag, IsInWorld);
            }
            else if (existing.GetEntry() != ClassicBankTabBagItem || !existing.IsBag())
            {
                Log.outError(LogFilter.PlayerItems, $"Cannot grant bank tab to {GetGUID()}: slot {InventorySlots.BankBagStart} contains item {existing.GetEntry()}.");
                return false;
            }
            if (GetCharacterBankTabCount() == 0)
                SetCharacterBankTabCount(1);
            return true;
        }

        public bool TryBuyCharacterBankTab()
        {
            byte count = GetCharacterBankTabCount();
            if (count == 0 || count >= MaxCharacterBankTabs)
                return false;
            BankTabRecord tab = CliDB.BankTabStorage.Values.FirstOrDefault(t => t.BankType == 0 && t.OrderIndex == count);
            if (tab == null || tab.Cost > long.MaxValue || !HasEnoughMoney((long)tab.Cost))
                return false;
            if (tab.PlayerConditionID != 0)
            {
                var condition = CliDB.PlayerConditionStorage.LookupByKey(tab.PlayerConditionID);
                if (condition == null || !ConditionManager.IsPlayerMeetingCondition(this, condition))
                    return false;
            }
            ModifyMoney(-(long)tab.Cost);
            SetCharacterBankTabCount((byte)(count + 1));
            return true;
        }

        void LoadClassicBankTabs(SQLResult result)
        {
            if (result.IsEmpty())
                return;
            SetCharacterBankTabCount(result.Read<byte>(0));
            do
            {
                if (result.IsNull(1))
                    continue;
                byte tab = result.Read<byte>(1);
                if (tab < GetCharacterBankTabCount())
                    SetCharacterBankTabSettings(tab, result.Read<string>(2), result.Read<string>(3), result.Read<string>(4), (BagSlotFlags)result.Read<uint>(5));
            } while (result.NextRow());
            _classicBankSettingsChanged = false;
        }

        void SaveClassicBankTabs(SQLTransaction transaction)
        {
            var statement = CharacterDatabase.GetPreparedStatement(CharStatements.UPD_CHAR_BANK_TAB_COUNT);
            statement.SetUInt8(0, GetCharacterBankTabCount());
            statement.SetInt64(1, GetGUID().GetCounter());
            transaction.Append(statement);
            if (!_classicBankSettingsChanged)
                return;
            statement = CharacterDatabase.GetPreparedStatement(CharStatements.DEL_CHAR_BANK_TAB_SETTINGS);
            statement.SetInt64(0, GetGUID().GetCounter()); transaction.Append(statement);
            for (byte tab = 0; tab < GetCharacterBankTabCount(); ++tab)
            {
                var settings = m_activePlayerData.CharacterBankTabSettings[tab];
                statement = CharacterDatabase.GetPreparedStatement(CharStatements.INS_CHAR_BANK_TAB_SETTINGS);
                statement.SetInt64(0, GetGUID().GetCounter()); statement.SetUInt8(1, tab);
                statement.SetString(2, settings.Name); statement.SetString(3, settings.Icon);
                statement.SetString(4, settings.Description); statement.SetUInt32(5, (uint)settings.DepositFlags.GetValue());
                transaction.Append(statement);
            }
            _classicBankSettingsChanged = false;
        }
    }
}
