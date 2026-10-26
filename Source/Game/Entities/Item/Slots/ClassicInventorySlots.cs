// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;

namespace Game.Entities
{
    /// <summary>
    /// Inventory slot numbering of the Forever 1.60.1 client (TrinityCore-forever Player.h) against this core's.
    ///
    ///                     this core   Classic 1.60 client
    ///   equipment         0-18        0-18
    ///   profession        19-29       19-29
    ///   bags              30-33       30-33
    ///   reagent bag       34          34
    ///   backpack          35-58       35-62
    ///   bank bags         63-71       63-71  (the bank's own tab + 8 bags)
    ///   buyback           94-105      72-83
    ///   keyring           106-137     -
    ///   child equipment   138-140     84-86
    ///   equipable spells  -           87-102
    ///   account bank bags -           103-107
    ///
    /// The keyring has no client equivalent and is not sent.
    /// Slot numbers are converted on the wire only: update fields, item packets read from the client, and
    /// ItemPushResult. Everything server-side keeps this core's numbering.
    /// </summary>
    public static class ClassicInventorySlots
    {
        /// <summary>ActivePlayerData.InvSlots size in the client.</summary>
        public const int Count = 145;

        /// <summary>Slots the ActivePlayerData changes mask can address (retail numbering, the rest only via create).</summary>
        public const int UpdateMaskCount = 108;

        const byte ClientItemEnd = 63;
        const byte ClientBuyBackStart = 72;
        const byte ClientBuyBackEnd = 84;
        const byte ClientChildEquipmentStart = 84;
        const byte ClientChildEquipmentEnd = 87;

        /// <summary>Server slot to client slot; ItemSlot.Null when the client has no such slot.</summary>
        public static byte ToClient(byte slot)
        {
            if (slot < InventorySlots.ItemEnd)
                return slot;
            if (slot >= InventorySlots.BankBagStart && slot < InventorySlots.BankBagEnd)
                return slot;
            if (slot >= InventorySlots.BuyBackStart && slot < InventorySlots.BuyBackEnd)
                return (byte)(ClientBuyBackStart + slot - InventorySlots.BuyBackStart);
            if (slot >= InventorySlots.ChildEquipmentStart && slot < InventorySlots.ChildEquipmentEnd)
                return (byte)(ClientChildEquipmentStart + slot - InventorySlots.ChildEquipmentStart);
            return ItemSlot.Null;
        }

        /// <summary>Client slot to server slot; ItemSlot.Null when this core has no such slot.</summary>
        public static byte ToServer(byte slot)
        {
            if (slot == ItemSlot.Null)
                return slot;
            if (slot < InventorySlots.ItemEnd)
                return slot;
            if (slot < ClientItemEnd)
                return ItemSlot.Null;   // backpack slots past this core's 24
            if (slot >= InventorySlots.BankBagStart && slot < InventorySlots.BankBagEnd)
                return slot;
            if (slot >= ClientBuyBackStart && slot < ClientBuyBackEnd)
                return (byte)(InventorySlots.BuyBackStart + slot - ClientBuyBackStart);
            if (slot >= ClientChildEquipmentStart && slot < ClientChildEquipmentEnd)
                return (byte)(InventorySlots.ChildEquipmentStart + slot - ClientChildEquipmentStart);
            return ItemSlot.Null;
        }

        /// <summary>
        /// A (container, slot) pair: the slot is an inventory slot only in the player's own inventory (container 255);
        /// otherwise the container is the bag's inventory slot and the slot an index inside the bag.
        /// </summary>
        public static void ToServer(ref byte container, ref byte slot)
        {
            if (container == ItemSlot.Null)
                slot = ToServer(slot);
            else
                container = ToServer(container);
        }

        /// <inheritdoc cref="ToServer(ref byte, ref byte)"/>
        public static ItemPos ToServer(ItemPos pos)
        {
            byte container = pos.Container;
            byte slot = pos.Slot;
            ToServer(ref container, ref slot);
            return new(slot, container);
        }

        /// <summary>Server to client for a (container, slot) pair.</summary>
        public static void ToClient(ref byte container, ref byte slot)
        {
            if (container == ItemSlot.Null)
                slot = ToClient(slot);
            else
                container = ToClient(container);
        }
    }
}
