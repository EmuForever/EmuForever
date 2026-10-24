// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using System;
using System.Collections.Generic;

namespace Game.Entities
{
    // The client requests cleanup; normal inventory swaps send the resulting item updates.
    internal sealed class BagSorter
    {
        enum Category
        {
            Equipment, Consumable, TradeGoods, Reagent, QuestItem, Other, Junk, Max
        }

        readonly record struct SortSlot(ItemPos Position, byte BagSlot);
        readonly record struct SortItem(ObjectGuid Guid, Category Category, int Entry, ItemClass Class,
            int SubClass, ItemQuality Quality, int ItemLevel, int Count, byte OriginalBag, bool MayLeaveBag);

        readonly Player _player;
        readonly List<SortSlot> _slots = new();
        readonly List<SortItem> _items = new();
        readonly int[][] _preferences = new int[(int)Category.Max][];
        readonly bool[] _specializedBags = new bool[InventorySlots.ReagentBagEnd];
        int[] _slotOwner;

        public BagSorter(Player player) { _player = player; }

        public void Sort()
        {
            // Rebuild after each sweep: SwapItem can merge stacks or refuse a swap.
            for (int pass = 0; pass < 3; ++pass)
            {
                Collect();
                if (_items.Count == 0)
                    return;

                if (!BuildSortedPlan() || !ApplyPlan())
                    return;
            }
        }

        static Category GetCategory(ItemTemplate proto)
        {
            // Class precedes quality: a poor-quality quest item remains a quest item.
            return proto.GetClass() switch
            {
                ItemClass.Quest => Category.QuestItem,
                ItemClass.Armor or ItemClass.Weapon => Category.Equipment,
                ItemClass.Consumable => Category.Consumable,
                ItemClass.TradeGoods => Category.TradeGoods,
                ItemClass.Reagent => Category.Reagent,
                _ when proto.GetBagFamily() != 0 => Category.Reagent,
                _ when proto.GetQuality() == ItemQuality.Poor => Category.Junk,
                _ => Category.Other
            };
        }

        static BagSlotFlags GetPriorityFlag(Category category) => category switch
        {
            Category.Equipment => BagSlotFlags.PriorityEquipment,
            Category.Consumable => BagSlotFlags.PriorityConsumables,
            Category.TradeGoods => BagSlotFlags.PriorityTradeGoods,
            Category.Reagent => BagSlotFlags.PriorityReagents,
            Category.QuestItem => BagSlotFlags.PriorityQuestItems,
            Category.Junk => BagSlotFlags.PriorityJunk,
            _ => BagSlotFlags.None
        };

        static int CompareItems(SortItem left, SortItem right)
        {
            int order = left.Category.CompareTo(right.Category);
            if (order != 0) return order;
            order = left.Class.CompareTo(right.Class);
            if (order != 0) return order;
            order = left.SubClass.CompareTo(right.SubClass);
            if (order != 0) return order;
            order = right.Quality.CompareTo(left.Quality);
            if (order != 0) return order;
            order = right.ItemLevel.CompareTo(left.ItemLevel);
            if (order != 0) return order;
            order = left.Entry.CompareTo(right.Entry);
            if (order != 0) return order;
            order = right.Count.CompareTo(left.Count);
            if (order != 0) return order;
            return left.Guid == right.Guid ? 0 : left.Guid < right.Guid ? -1 : 1;
        }

        void Collect()
        {
            _slots.Clear();
            _items.Clear();
            Array.Clear(_specializedBags);
            AddContainer(InventorySlots.Bag0, InventorySlots.ItemStart, _player.GetInventorySlotCount(), false);

            for (byte bagSlot = InventorySlots.BagStart; bagSlot < InventorySlots.ReagentBagEnd; ++bagSlot)
            {
                Bag bag = _player.GetBagByPos(bagSlot);
                if (bag == null || _player.GetBagSlotFlags(bagSlot - InventorySlots.BagStart).HasAnyFlag(BagSlotFlags.DisableAutoSort))
                    continue;

                ItemTemplate proto = bag.GetTemplate();
                _specializedBags[bagSlot] = proto == null || proto.GetClass() != ItemClass.Container
                    || (proto.GetSubClass().Container != ItemSubClassContainer.Container
                        && proto.GetSubClass().Container != ItemSubClassContainer.ReagentContainer);
                AddContainer(bagSlot, 0, (int)bag.GetBagSize(), _player.IsSortBagsRightToLeft());
            }
        }

        void AddContainer(byte bagSlot, byte firstSlot, int count, bool rightToLeft)
        {
            bool specialized = bagSlot != InventorySlots.Bag0 && _specializedBags[bagSlot];
            for (int i = 0; i < count; ++i)
            {
                int slot = rightToLeft ? count - 1 - i : i;
                ItemPos position = new((byte)(firstSlot + slot), bagSlot);
                Item item = _player.GetItemByPos(position);
                ItemTemplate proto = item?.GetTemplate();
                if (item != null && (proto == null || item.HasItemFlag(ItemFieldFlags.Child) || item.IsNotEmptyBag()))
                    continue;

                _slots.Add(new(position, bagSlot));
                if (item != null)
                    _items.Add(new(item.GetGUID(), GetCategory(proto), proto.GetId(), proto.GetClass(), proto.GetSubClass().data,
                        proto.GetQuality(), proto.GetItemLevel(), item.GetCount(), bagSlot, !specialized));
            }
        }

        void BuildPreference(Category category)
        {
            int[] order = new int[_slots.Count];
            for (int i = 0; i < order.Length; ++i)
                order[i] = i;

            // Preserve container/slot order within a rank, including the direction setting.
            Array.Sort(order, (left, right) =>
            {
                int rank = GetSlotRank(_slots[left], category).CompareTo(GetSlotRank(_slots[right], category));
                return rank != 0 ? rank : left.CompareTo(right);
            });
            _preferences[(int)category] = order;
        }

        int GetSlotRank(SortSlot slot, Category category)
        {
            if (slot.BagSlot == InventorySlots.Bag0)
                return 2;
            if (slot.BagSlot == InventorySlots.ReagentBagStart && category == Category.Reagent)
                return 0;
            BagSlotFlags priority = GetPriorityFlag(category);
            if (priority != BagSlotFlags.None && _player.GetBagSlotFlags(slot.BagSlot - InventorySlots.BagStart).HasAnyFlag(priority))
                return 0;
            return _specializedBags[slot.BagSlot] ? 1 : 3;
        }

        bool CanGoIntoSlot(ItemTemplate proto, SortSlot slot)
        {
            if (slot.BagSlot == InventorySlots.Bag0)
                return true;
            Bag bag = _player.GetBagByPos(slot.BagSlot);
            return bag != null && Item.ItemCanGoIntoBag(proto, bag.GetTemplate());
        }

        bool BuildPlan()
        {
            _slotOwner = new int[_slots.Count];
            Array.Fill(_slotOwner, -1);
            bool[] visited = new bool[_slots.Count];
            // Augmenting an assignment displaces its old owner. Insert the highest priority
            // items last so that they keep the best slots instead of reversing the sort.
            for (int itemIndex = _items.Count - 1; itemIndex >= 0; --itemIndex)
            {
                Array.Clear(visited);
                if (!TryAssign(itemIndex, visited))
                {
                    Log.outError(LogFilter.Network, $"BagSorter: no slot found for item {_items[itemIndex].Entry} ({_items[itemIndex].Guid})");
                    return false;
                }
            }
            return true;
        }

        bool BuildSortedPlan()
        {
            _items.Sort(CompareItems);
            for (int category = 0; category < (int)Category.Max; ++category)
                BuildPreference((Category)category);
            return BuildPlan();
        }

        bool TryAssign(int itemIndex, bool[] visited)
        {
            SortItem item = _items[itemIndex];
            Item itemObject = _player.GetItemByGuid(item.Guid);
            if (itemObject == null)
                return false;

            foreach (int slotIndex in _preferences[(int)item.Category])
            {
                SortSlot slot = _slots[slotIndex];
                if (visited[slotIndex] || (!item.MayLeaveBag && slot.BagSlot != item.OriginalBag)
                    || !CanGoIntoSlot(itemObject.GetTemplate(), slot))
                    continue;

                visited[slotIndex] = true;
                int owner = _slotOwner[slotIndex];
                if (owner == -1 || TryAssign(owner, visited))
                {
                    _slotOwner[slotIndex] = itemIndex;
                    return true;
                }
            }
            return false;
        }

        bool ApplyPlan()
        {
            bool changed = false;
            for (int slotIndex = 0; slotIndex < _slots.Count; ++slotIndex)
            {
                int itemIndex = _slotOwner[slotIndex];
                if (itemIndex == -1)
                    continue;

                ObjectGuid wantedGuid = _items[itemIndex].Guid;
                SortSlot slot = _slots[slotIndex];
                Item current = _player.GetItemByPos(slot.Position);
                if (current != null && current.GetGUID() == wantedGuid)
                    continue;
                Item wanted = _player.GetItemByGuid(wantedGuid);
                if (wanted == null)
                    continue; // Merged into another stack by an earlier swap.

                // A swap must not temporarily eject an existing item from its family bag.
                if (current != null && slot.BagSlot != InventorySlots.Bag0 && _specializedBags[slot.BagSlot]
                    && wanted.InventoryBagSlot != slot.BagSlot)
                    continue;

                ItemPos source = wanted.InventoryPosition;
                int count = wanted.GetCount();
                _player.SwapItem(source, slot.Position);
                changed |= _player.GetItemByPos(source) != wanted || wanted.GetCount() != count;
            }
            return changed;
        }
    }
}
