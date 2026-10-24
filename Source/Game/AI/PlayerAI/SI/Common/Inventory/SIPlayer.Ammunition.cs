using Framework.Constants;
using Game.Entities;
using System;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        long _nextAmmoVendorSearch;
        bool UsesAmmunition() => me.GetClass() == Class.Hunter && !me.HasAura(46699) &&
            me.GetWeaponForAttack(WeaponAttackType.RangedAttack)?.GetTemplate().GetSubClass().Weapon is
                ItemSubClassWeapon.Bow or ItemSubClassWeapon.Crossbow or ItemSubClassWeapon.Gun;

        bool UsableAmmunition(ItemTemplate item) => item != null && item.GetClass() == ItemClass.Projectile &&
            me.CheckAmmoCompatibility(item) && me.CanUseAmmo(item.GetId()) == InventoryResult.Ok;

        int AmmunitionCount()
        {
            int count = 0;
            me.ForEachItem(ItemSearchLocation.Inventory, item =>
            {
                if (UsableAmmunition(item.GetTemplate())) count += (int)item.GetCount();
                return true;
            });
            return count;
        }

        void SelectAmmunition()
        {
            if (!UsesAmmunition()) return;
            if (me.GetItemCount(me.GetUsedAmmoId()) > 0 && UsableAmmunition(Global.ObjectMgr.GetItemTemplate(me.GetUsedAmmoId()))) return;
            int selected = 0;
            me.ForEachItem(ItemSearchLocation.Inventory, item =>
            {
                if (UsableAmmunition(item.GetTemplate())) selected = item.GetEntry();
                return selected == 0;
            });
            if (selected != 0) me.SetAmmo(selected);
        }

        bool HasAmmunition() => !UsesAmmunition() ||
            me.GetItemCount(me.GetUsedAmmoId()) > 0 && UsableAmmunition(Global.ObjectMgr.GetItemTemplate(me.GetUsedAmmoId()));
        bool NeedsAmmunition() => UsesAmmunition() && AmmunitionCount() < 40;

        (ItemTemplate Item, int Quantity) AmmunitionPurchase(int vendorEntry)
        {
            var items = Global.ObjectMgr.GetNpcVendorItemList(vendorEntry);
            ItemTemplate best = null;
            int quantity = 0, needed = 200 - AmmunitionCount();
            for (int slot = 0; items != null && slot < items.GetItemCount(); ++slot)
            {
                var entry = items.GetItem(slot);
                if (entry == null || entry.Type != ItemVendorType.Item || entry.ExtendedCost != 0) continue;
                ItemTemplate item = Global.ObjectMgr.GetItemTemplate(entry.item);
                if (!UsableAmmunition(item)) continue;
                int available = SIVendorPolicy.PurchaseQuantity(needed, item.GetBuyCount(), item.GetBuyPrice(), me.GetMoney());
                if (available == 0) continue;
                if (best == null || (decimal)item.GetBuyPrice() / Math.Max(1, item.GetBuyCount()) <
                    (decimal)best.GetBuyPrice() / Math.Max(1, best.GetBuyCount()))
                { best = item; quantity = available; }
            }
            return (best, quantity);
        }

        bool BuyAmmunition(Creature vendor)
        {
            var purchase = AmmunitionPurchase(vendor.GetEntry());
            if (purchase.Item == null) return false;
            int before = AmmunitionCount();
            if (!me.GetSession().BuyItemForSI(vendor, purchase.Item.GetId(), (byte)purchase.Quantity) || AmmunitionCount() <= before) return false;
            SelectAmmunition();
            NoteGameplayProgress();
            Report("ammo_purchase", $"Bought {AmmunitionCount() - before} ammunition from {vendor.GetName()}");
            return true;
        }

        void StopOutOfRangeHunterShots()
        {
            if (me.GetClass() != Class.Hunter) return;
            Unit target = me.GetVictim();
            foreach (var kind in new[] { CurrentSpellTypes.AutoRepeat, CurrentSpellTypes.Generic })
            {
                var cast = me.GetCurrentSpell(kind);
                if (cast == null || !cast.m_spellInfo.IsRangedWeaponSpell()) continue;
                if (target == null || HunterNeedsMelee(target) || cast.CheckUnitTargetRange(target) != SpellCastResult.SpellCastOk)
                    me.InterruptSpell(kind);
            }
        }

        bool HunterNeedsMelee(Unit target) => me.GetClass() == Class.Hunter &&
            (!HasAmmunition() || target != null && me.GetDistance(target) <= Math.Max(8, me.GetMeleeRange(target)));
    }
}
