using Framework.Constants;
using System;

namespace Game.AI
{
    public enum SIItemDisposition { Carry, Vendor, Bank, Auction }

    public static class SIInventoryPolicy
    {
        public static bool IsVendorQuality(ItemClass itemClass, ItemQuality quality) =>
            quality == ItemQuality.Poor || quality == ItemQuality.Normal && itemClass is ItemClass.Weapon or ItemClass.Armor;

        public static SIItemDisposition Disposition(ItemClass itemClass, ItemQuality quality, bool protectedItem,
            bool currentUpgrade, bool futureUpgrade, bool bound, bool auctionable)
        {
            if (protectedItem || currentUpgrade) return SIItemDisposition.Carry;
            if (IsVendorQuality(itemClass, quality)) return SIItemDisposition.Vendor;
            if (futureUpgrade || bound || quality != ItemQuality.Normal && itemClass is ItemClass.TradeGoods or ItemClass.Gem or ItemClass.Recipe)
                return SIItemDisposition.Bank;
            return auctionable ? SIItemDisposition.Auction : SIItemDisposition.Bank;
        }

        public static bool ShouldVisit(uint freeSlots, bool nearby, bool hasItems,int reserveSlots=0) => hasItems && (nearby || freeSlots <= Math.Max(0,reserveSlots));

        // Deterministic asking price until market valuation is available: twice vendor value,
        // with a one-silver minimum per item. Both AH packet types use copper.
        public static long AuctionUnitPrice(long vendorPrice) =>
            (long)Math.Clamp((decimal)vendorPrice * 2, 100, PlayerConst.MaxMoneyAmount);

        public static bool CanPayDeposit(long money, long repairReserve, long deposit) =>
            deposit > 0 && deposit <= Math.Max(0, money / 2 - Math.Max(0, repairReserve));
    }
}
