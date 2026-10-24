using System;

namespace Game.AI
{
    public enum SIVendorPurpose { Sell, Repair, Restock, Ammunition }

    public static class SIVendorPolicy
    {
        public static bool ShouldFundRepair(bool repairNeeded, long repairCost, long money, bool hasJunk) =>
            repairNeeded && repairCost > money && hasJunk;
        public static bool ShouldSell(uint freeSlots, int totalSlots, bool hasJunk, bool vendorInRange, int reserveSlots) =>
            hasJunk && vendorInRange && totalSlots > 0;
        public static bool BagPressure(uint freeSlots,int reserveSlots,bool hasJunk)=>hasJunk&&reserveSlots>0&&freeSlots<=reserveSlots;
        public static bool LocalSaleRange(float distance)=>float.IsFinite(distance)&&distance>=0&&distance<=20;
        public static bool ShouldLeaveForRepair(int broken, int repairable) => repairable > 0 && broken == repairable;

        // The core's BuyItemFromVendorSlot takes item units. Template price is for BuyCount units.
        // Keep half the character's money for repairs/training and never exceed the packet byte count.
        public static int PurchaseQuantity(int needed, int buyCount, long buyPrice, long money)
        {
            if (needed <= 0 || buyCount <= 0 || buyPrice < 0 || money < 0) return 0;
            if (buyPrice == 0) return Math.Min(255, needed);
            decimal unitPrice = (decimal)buyPrice / buyCount;
            decimal affordable = Math.Floor((money / 2) / unitPrice);
            return (int)Math.Min(Math.Min(needed, 255), affordable);
        }
        public static bool RepairNeeded(int current, int maximum, float minimum) => maximum > 0 && (float)current / maximum <= minimum;
    }
}
