using Framework.Constants;
using Game.AI;
using Game.Entities;
using Game.Networking.Packets;
using System;
using System.Collections.Generic;

namespace Game
{
    public partial class WorldSession
    {
        // CMSG_TRAINER_BUY_SPELL at a profession trainer, for the lessons of the professions the caller wants.
        internal int TrainProfessionForSI(Creature trainer, Func<TrainerSpell, bool> wanted)
        {
            if (!IsSIControlled || trainer == null) return 0;
            Creature npc = _player.GetNPCIfCanInteractWith(trainer.GetGUID(), NPCFlags1.Trainer, NPCFlags2.None);
            if (npc == null) return 0;
            int learned = 0;
            foreach (int id in Global.ObjectMgr.GetCreatureTrainerIds(npc.GetEntry()))
            {
                Trainer data = Global.ObjectMgr.GetTrainer(id);
                if (data == null || data.GetTrainerType() != TrainerType.Tradeskills) continue;
                foreach (TrainerSpell spell in data.GetTeachableSpells(_player))
                {
                    if (!wanted(spell) ||
                        !_player.HasEnoughMoney((long)(spell.MoneyCost * _player.GetReputationPriceDiscount(npc)))) continue;
                    int known = _player.GetSpellMap().Count;
                    data.TeachSpell(npc, _player, spell.SpellId);
                    if (_player.GetSpellMap().Count != known || _player.HasSpell(spell.SpellId)) ++learned;
                }
            }
            return learned;
        }

        // CMSG_AUTOSTORE_BANK_ITEM: bank (main tab or a bank bag) to the bags.
        internal bool WithdrawBankItemForSI(Creature banker, Item item,int count=int.MaxValue)
        {
            if (!IsSIControlled || _player == null || banker == null || item == null || !item.InventoryPosition.IsBankPos ||
                item.InventoryPosition.IsBagSlotPos || count<=0 || !OpenBankForSI(banker)) return false;
            count=Math.Min(count,item.GetCount());
            var capacity=_player.CanStoreItem(ItemPos.Undefined,out _,out int missing,item,null,count);
            if(capacity!=InventoryResult.Ok)
            {
                if(capacity!=InventoryResult.InvFull||missing>=count)return false;
                count-=missing;
            }
            int entry=item.GetEntry(),before=_player.GetItemCount(entry);
            if(count<item.GetCount())
            {
                _player.SplitItem(item.InventoryPosition,ItemPos.Undefined,count);
                int gained=_player.GetItemCount(entry)-before;
                if(gained>0)_player.ItemAddedQuestCheck(entry,gained);
            }
            else HandleAutoStoreBankItem(new AutoStoreBankItem(null) { Bag = item.InventoryBagSlot, Slot = item.InventorySlot });
            return _player.GetItemCount(entry)>before;
        }

        // CMSG_BUY_ACCOUNT_BANK_TAB for the character bank: the next Classic bank bag slot.
        internal bool BuyBankTabForSI(Creature banker, long reserve)
        {
            if (!IsSIControlled || _player == null || !OpenBankForSI(banker)) return false;
            byte count = _player.GetCharacterBankTabCount();
            var tab = SIPlayer.NextBankTab(count);
            if (tab == null || (long)tab.Cost > _player.GetMoney() - Math.Max(0, reserve)) return false;
            HandleBuyBankTab(new BuyBankTab(null) { Banker = banker.GetGUID(), BankType = 0 });
            return _player.GetCharacterBankTabCount() > count;
        }

        // Moves an empty bag from the bags into the first unlocked, empty bank bag slot.
        internal bool BankBagForSI(Creature banker, Item bag)
        {
            if (!IsSIControlled || _player == null || bag == null || !bag.IsBag() || bag.IsNotEmptyBag() ||
                !bag.InventoryPosition.IsInventoryPos || !OpenBankForSI(banker)) return false;
            for (byte slot = InventorySlots.BankBagStart; slot < InventorySlots.BankBagEnd; ++slot)
            {
                if (slot - InventorySlots.BankBagStart >= _player.GetCharacterBankTabCount()) break;
                if (_player.GetItemByPos(slot) != null) continue;
                if (_player.CanBankItem(new ItemPos(slot), out List<ItemPosCount> dest, bag) != InventoryResult.Ok) continue;
                _player.RemoveItem(bag.InventoryPosition, true);
                _player.BankItem(dest, bag, true);
                return true;
            }
            return false;
        }

        bool OpenBankForSI(Creature banker)
        {
            if (banker == null || _player.GetNPCIfCanInteractWith(banker.GetGUID(), NPCFlags1.Banker, NPCFlags2.None) == null) return false;
            HandleBankerActivate(new BankerActivate(null) { Banker = banker.GetGUID(), InteractionType = PlayerInteractionType.CharacterBanker });
            return CanUseBank(banker.GetGUID());
        }
    }
}
