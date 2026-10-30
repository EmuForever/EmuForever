using Framework.Constants;
using Game.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        internal bool NeedsQuestLoot(int item) => QuestNeedsItem(item);

        // Bag items (backpack and equipped bags) matching a filter.
        List<Item> InventoryItems(Func<Item, bool> match)
        {
            var items = new List<Item>();
            me.ForEachItem(ItemSearchLocation.Inventory, item => { if (match(item)) items.Add(item); return true; });
            return items;
        }

        Item FirstInventoryItem(Func<Item, bool> match)
        {
            Item found = null;
            me.ForEachItem(ItemSearchLocation.Inventory, item => { if (match(item)) found = item; return found == null; });
            return found;
        }

        // Total stack count of the bag items matching a filter.
        int InventoryCount(Func<Item, bool> match)
        {
            int count = 0;
            me.ForEachItem(ItemSearchLocation.Inventory, item => { if (match(item)) count += (int)item.GetCount(); return true; });
            return count;
        }

        List<Item> DisposableJunk() => InventoryItems(item =>
        {
            var template = item.GetTemplate();
            return item.GetQuality() == ItemQuality.Poor && !item.IsBag() && !item.IsInTrade() &&
                !template.HasFlag(ItemFlags.NoUserDestroy) && !QuestNeedsItem(item.GetEntry()) &&
                template.GetStartQuest() == 0 && !Provision(template, false) && !Provision(template, true);
        }).OrderBy(item => (long)item.GetTemplate().GetSellPrice() * item.GetCount()).ToList();

        bool CanMakeSourceItemSpace(Quest quest) => quest.SourceItemId > 0 && QuestLogCount() < SharedConst.MaxQuestLogSize &&
            me.CanStoreNewItem(ItemPos.Undefined, out _, quest.SourceItemId, Math.Max(1, quest.SourceItemIdCount)) == InventoryResult.InvFull &&
            DisposableJunk().Count != 0;

        // Called only for an actual incoming quest item, never just because bags are full.
        // CanStoreNewItem accounts for existing partial stacks and unique-item restrictions.
        internal bool EnsureQuestItemSpace(int item, int count, bool sourceItem = false)
        {
            if (count <= 0 || !sourceItem && !QuestNeedsItem(item)) return false;
            InventoryResult capacity = me.CanStoreNewItem(ItemPos.Undefined, out _, item, count);
            if (capacity == InventoryResult.Ok || sourceItem && capacity == InventoryResult.ItemMaxCount) return true;
            if (capacity != InventoryResult.InvFull) return false;
            foreach (Item discard in DisposableJunk())
            {
                int entry = discard.GetEntry(), quantity = (int)discard.GetCount();
                if (!me.GetSession().DestroyJunkForSI(discard)) continue;
                Report("quest_bag_space", $"Discarded {quantity}x junk {entry} for quest item {item}");
                capacity = me.CanStoreNewItem(ItemPos.Undefined, out _, item, count);
                if (capacity == InventoryResult.Ok) return true;
                if (capacity != InventoryResult.InvFull) return false;
            }
            return false;
        }
    }
}
