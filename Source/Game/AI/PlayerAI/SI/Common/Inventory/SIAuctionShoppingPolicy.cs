using Framework.Constants;
using System;

namespace Game.AI
{
    public sealed class SIAuctionShoppingBudget
    {
        long _windowAt = -1, _allowance, _spent;
        int _purchases;
        public long Remaining(long now, long money, long reserve)
        {
            long available = Math.Max(0, money - Math.Max(0, reserve));
            if (_windowAt < 0 || now < _windowAt || now - _windowAt >= 30 * 60000)
            { _windowAt = now; _allowance = available / 5; _spent = 0; _purchases = 0; }
            // A penniless login is not a spent shopping window. Newly earned
            // gold can fund its first purchase without waiting thirty minutes.
            if (_allowance == 0 && _spent == 0 && _purchases == 0) _allowance = available / 5;
            return _purchases >= 4 ? 0 : Math.Min(available, Math.Max(0, _allowance - _spent));
        }
        public void Record(long price)
        {
            if (price <= 0 || price > _allowance - _spent) throw new ArgumentOutOfRangeException(nameof(price));
            _spent += price; ++_purchases;
        }
    }

    public static class SIAuctionShoppingPolicy
    {
        public static long UnitPriceLimit(long sellPrice, long buyPrice, bool equipment) =>
            (long)Math.Clamp(Math.Max(equipment ? 10000m : 100m, Math.Max((decimal)sellPrice * 20, (decimal)buyPrice * 4)), 1, PlayerConst.MaxMoneyAmount);
        public static int MaterialTarget(int reagentCount) => reagentCount <= 0 ? 0 : (int)Math.Min(20L, (long)reagentCount * 2);
        public static bool RecipeUseful(int skill, int required, int trivial) => skill > 0 && skill >= required && trivial > skill;
        public static bool PurchaseAllowed(long unitPrice, int quantity, long unitLimit, long budget) =>
            unitPrice > 0 && quantity > 0 && unitPrice <= unitLimit && (decimal)unitPrice * quantity <= budget;
        public static bool MayScanMaterial(long now, long? lastScan) => !lastScan.HasValue || now < lastScan.Value || now - lastScan.Value >= 600000;
    }
}
