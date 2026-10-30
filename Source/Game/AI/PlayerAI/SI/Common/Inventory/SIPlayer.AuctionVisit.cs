using Framework.Constants;
using Game.Entities;
using System;
using System.Threading.Tasks;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        SITask _auctionVisit;
        Task<(bool Success, long Spent)> _auctionVisitBuy;
        Task<bool> _auctionVisitPost;
        AuctionPurchase _auctionVisitPurchase;
        ObjectGuid _auctionVisitItem;
        bool _auctionBuyRecorded, _auctionListingsComplete, _auctionBuyStarted;
        int _auctionListed;
        internal bool OwnsAuctionVisit(SITask visit) => visit != null && ReferenceEquals(_task, visit) &&
            visit.Kind == SITaskKind.Vendor && visit.VendorPurpose == SIVendorPurpose.Auction && !visit.InteractionCompleted;

        SIBehaviorResult AuctionAt(Creature npc, SITask task)
        {
            if (npc == null || me.GetNPCIfCanInteractWith(npc.GetGUID(), NPCFlags1.Auctioneer, NPCFlags2.None) == null) return SIBehaviorResult.Failure;
            if (!ReferenceEquals(_auctionVisit, task))
            {
                _auctionVisit = task; _auctionVisitBuy = null; _auctionVisitPost = null;
                _auctionBuyRecorded = _auctionListingsComplete = _auctionBuyStarted = false;_auctionListed=0;
            }
            // Free bag space before shopping, and finish every eligible listing
            // during this visit instead of leaving after the first crafted item.
            if(!_auctionListingsComplete)
            {
                var listing=PostAuctionSurplus(npc,task);
                if(listing!=SIBehaviorResult.Success)return listing;
                _auctionListingsComplete=true;
            }
            if(!_auctionBuyStarted)
            {
                _auctionBuyStarted=true;
                _auctionVisitPurchase = NeededAuctionPurchase(npc, force: true);
                if (_auctionVisitPurchase is { } purchase)
                    _auctionVisitBuy = me.GetSession().QueueAuctionBuyForSI(npc, purchase.Auction, purchase.Item, purchase.Quantity,
                        Math.Min(purchase.Price, ShoppingAllowance()), ShoppingReserve(), task);
            }
            if (_auctionVisitBuy != null)
            {
                if (!_auctionVisitBuy.IsCompleted) { Status = "Submitting auction purchase"; return SIBehaviorResult.Running; }
                if (!_auctionVisitBuy.IsCompletedSuccessfully) return SIBehaviorResult.Failure;
                bool bought = _auctionVisitBuy.Result.Success;
                _auctionBuyRecorded |= bought;
                if (me.GetSession().HasPendingSIAuctionPurchase) { Status = "Waiting for auction purchase to commit"; return SIBehaviorResult.Running; }
                _auctionVisitBuy = null;
                // A profession visit buys all useful affordable lots allowed by
                // its existing four-purchase budget, not just one stack per trip.
                if (bought && _professionShopping.Count > 0 && _goal is "professions" or "social")
                {
                    var next = NeededAuctionPurchase(npc, force: true);
                    if (next != null)
                    {
                        _auctionVisitPurchase = next;
                        _auctionVisitBuy = me.GetSession().QueueAuctionBuyForSI(npc, next.Auction, next.Item, next.Quantity,
                            Math.Min(next.Price, ShoppingAllowance()), ShoppingReserve(), task);
                        Status = "Buying remaining profession materials";
                        return SIBehaviorResult.Running;
                    }
                }
            }
            Status = _auctionBuyRecorded ? $"Listed {_auctionListed} item(s); bought profession items; collecting auction mail" : $"Listed {_auctionListed} surplus item(s); auction visit completed";
            return SIBehaviorResult.Success;
        }

        SIBehaviorResult PostAuctionSurplus(Creature npc,SITask task)
        {
            if (_auctionVisitPost != null)
            {
                if (!_auctionVisitPost.IsCompleted) { Status = "Submitting auction listing"; return SIBehaviorResult.Running; }
                if (!_auctionVisitPost.IsCompletedSuccessfully) return SIBehaviorResult.Failure;
                if (_auctionVisitPost.Result) {NoteGameplayProgress();++_auctionListed;}
                else _storageItemRetryAt[_auctionVisitItem] = Now + 60000;
                _auctionVisitPost=null;
            }
            var items=StorageItems(SIVendorPurpose.Auction,npc.GetFaction());
            if(items.Count==0)return SIBehaviorResult.Success;
            _auctionVisitItem=items[0].GetGUID();
            _auctionVisitPost=me.GetSession().QueueAuctionPostForSI(npc,items[0],RequiredRepairCost(),task);
            Status=$"Listing surplus item {_auctionListed+1}; {items.Count} eligible item(s) remaining";
            return SIBehaviorResult.Running;
        }
    }
}
