// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.DataStorage;
using Game.Entities;
using Game.Networking;
using Game.Networking.Packets;
using System.Collections.Generic;

namespace Game
{
    public partial class WorldSession
    {
        [WorldPacketHandler(ClientOpcodes.AutobankItem, Processing = PacketProcessing.Inplace)]
        void HandleAutoBankItem(AutoBankItem packet)
        {
            if (!CanUseBank())
            {
                Log.outDebug(LogFilter.Network, $"WORLD: HandleAutoBankItemOpcode - {_player.PlayerTalkClass.GetInteractionData().SourceGuid} not found or you can't interact with him.");
                return;
            }

            if (packet.BankType != 0)
                return;

            ItemPos itemPos = new(packet.Slot, packet.Bag);
            Item item = GetPlayer().GetItemByPos(itemPos);
            if (item == null)
                return;

            if (itemPos.IsInventoryPos) // move only from inventory (not equippable slots)
            {
                InventoryResult msg = GetPlayer().CanBankItem(ItemPos.Undefined, out var dest, item);
                if (msg != InventoryResult.Ok)
                {
                    GetPlayer().SendEquipError(msg, item);
                    return;
                }

                GetPlayer().RemoveItem(itemPos, true);
                GetPlayer().ItemRemovedQuestCheck(item.GetEntry(), item.GetCount());
                GetPlayer().BankItem(dest, item, true);
            }
        }

        [WorldPacketHandler(ClientOpcodes.BankerActivate, Processing = PacketProcessing.Inplace)]
        void HandleBankerActivate(BankerActivate packet)
        {
            if (packet.InteractionType != PlayerInteractionType.Banker && packet.InteractionType != PlayerInteractionType.CharacterBanker)
                return;
            Creature unit = GetPlayer().GetNPCIfCanInteractWith(packet.Banker, NPCFlags1.Banker, NPCFlags2.None);
            if (unit == null)
            {
                Log.outError(LogFilter.Network, $"HandleBankerActivate: {packet.Banker} not found or you can not interact with him.");
                return;
            }

            if (GetPlayer().HasUnitState(UnitState.Died))
                GetPlayer().RemoveAurasByType(AuraType.FeignDeath);

            SendShowBank(packet.Banker, packet.InteractionType);
        }

        [WorldPacketHandler(ClientOpcodes.AutostoreBankItem, Processing = PacketProcessing.Inplace)]
        void HandleAutoStoreBankItem(AutoStoreBankItem packet)
        {
            if (!CanUseBank())
            {
                Log.outDebug(LogFilter.Network, $"WORLD: HandleAutoBankItemOpcode - {_player.PlayerTalkClass.GetInteractionData().SourceGuid} not found or you can't interact with him.");
                return;
            }

            ItemPos itemPos = new(packet.Slot, packet.Bag);
            Item item = GetPlayer().GetItemByPos(itemPos);
            if (item == null)
                return;

            if (itemPos.IsBankPos && !itemPos.IsBagSlotPos) // moving from bank to inventory (not equippable slots)
            {
                InventoryResult msg = GetPlayer().CanStoreItem(ItemPos.Undefined, out var dest, item);
                if (msg != InventoryResult.Ok)
                {
                    GetPlayer().SendEquipError(msg, item);
                    return;
                }

                GetPlayer().RemoveItem(itemPos, true);
                GetPlayer().ItemAddedQuestCheck(item.GetEntry(), item.GetCount());
                GetPlayer().StoreItem(dest, item, true);
                    
            }
            else if (itemPos.IsInventoryPos)
            {
                InventoryResult result = GetPlayer().CanBankItem(ItemPos.Undefined, out var destination, item);
                if (result != InventoryResult.Ok)
                    GetPlayer().SendEquipError(result, item);
                else
                {
                    GetPlayer().RemoveItem(itemPos, true);
                    GetPlayer().ItemRemovedQuestCheck(item.GetEntry(), item.GetCount());
                    GetPlayer().BankItem(destination, item, true);
                }
            }
        }

        [WorldPacketHandler(ClientOpcodes.BuyAccountBankTab)]
        void HandleBuyBankTab(BuyBankTab packet)
        {
            if (packet.BankType != 0 || !CanUseBank(packet.Banker))
                return;
            GetPlayer().TryBuyCharacterBankTab();
        }

        [WorldPacketHandler(ClientOpcodes.UpdateAccountBankTabSettings)]
        void HandleUpdateBankTabSettings(UpdateBankTabSettings packet)
        {
            if (packet.BankType != 0 || !CanUseBank(packet.Banker) || packet.Tab >= _player.GetCharacterBankTabCount())
                return;
            _player.SetCharacterBankTabSettings(packet.Tab, packet.Name, packet.Icon, packet.Description, packet.DepositFlags);
        }

        [WorldPacketHandler(ClientOpcodes.AutoDepositCharacterBank)]
        void HandleAutoDepositCharacterBank(AutoDepositCharacterBank packet)
        {
            if (!CanUseBank(packet.Banker))
                return;
            foreach (Item item in _player.GetCraftingReagentItemsToDeposit())
            {
                InventoryResult result = _player.CanBankItem(ItemPos.Undefined, out var destination, item);
                if (result != InventoryResult.Ok)
                {
                    _player.SendEquipError(result, item);
                    break;
                }
                _player.RemoveItem(item.InventoryPosition, true);
                _player.ItemRemovedQuestCheck(item.GetEntry(), item.GetCount());
                _player.BankItem(destination, item, true);
            }
        }

        public void SendShowBank(ObjectGuid guid, PlayerInteractionType interactionType = PlayerInteractionType.CharacterBanker)
        {
            if (!_player.GrantClassicFreeBankTab())
                return;
            _player.PlayerTalkClass.GetInteractionData().Reset();
            _player.PlayerTalkClass.GetInteractionData().SourceGuid = guid;
            NPCInteractionOpenResult npcInteraction = new();
            npcInteraction.Npc = guid;
            npcInteraction.InteractionType = interactionType;
            npcInteraction.Success = true;
            SendPacket(npcInteraction);
        }
    }
}
