using Framework.Constants;
using Game.Entities;
using System;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        SISpawn? _auctionMailDestination;
        ObjectGuid _auctionMailbox;
        bool _awaitingPurchasedMail;
        long _nextAuctionMailScan, _mailProgressAt;
        float _mailClosest;

        bool HasAuctionMail() => me.GetMails().Any(mail => mail.messageType == MailMessageType.Auction && mail.state != MailState.Deleted &&
            mail.deliver_time <= LoopTime.ServerTime && mail.COD == 0 && (mail.money > 0 || mail.items.Any(attachment =>
                me.GetMItem(attachment.item_guid) is Item item && me.CanStoreItem(ItemPos.Undefined, out _, item) == InventoryResult.Ok)));

        bool TickAuctionMail()
        {
            if(CommittedToJourney()&&!_auctionMailDestination.HasValue&&!_awaitingPurchasedMail)return false;
            if (_task?.Kind is SITaskKind.Vendor or SITaskKind.Train) return false;
            if (me.GetSession().HasPendingSIAuctionPurchase)
            { _task?.Suspend(Now); Status = "Waiting for auction purchase to commit"; return true; }
            if (!_auctionMailDestination.HasValue)
            {
                if (Now < _nextAuctionMailScan) { _awaitingPurchasedMail = false; return false; }
                _nextAuctionMailScan = Now + 10000;
                if (!HasAuctionMail()) { _awaitingPurchasedMail = false; return false; }
                WorldObject mailbox = NearbyObjects(QuestPickupRadius).Where(obj => obj.GetGoType() == GameObjectTypes.Mailbox && obj.IsSpawned())
                    .Cast<WorldObject>().Concat(NearbyCreatures(QuestPickupRadius).Where(npc => npc.IsAlive() && npc.HasNpcFlag(NPCFlags1.Mailbox) &&
                        !me.IsHostileTo(npc) && !npc.IsHostileTo(me))).Where(obj => SocialPointAllowed(V(obj.GetPosition())) && me.CanSeeOrDetect(obj, distanceCheck: true))
                    .OrderBy(me.GetExactDist).FirstOrDefault();
                _auctionMailDestination = mailbox != null ? SpawnOf(mailbox) : _awaitingPurchasedMail && SIQuestKnowledge.IsBuilt
                    ? NearestSpawn(Knowledge.MailboxCreatures, Knowledge.MailboxObjects, true) : null;
                _auctionMailbox = mailbox?.GetGUID() ?? ObjectGuid.Empty;
                if (!_auctionMailDestination.HasValue) { _awaitingPurchasedMail = false; return false; }
                MarkPartyErrand(); _mailProgressAt = Now; _mailClosest = float.MaxValue;
            }
            _task?.Suspend(Now);
            SISpawn spawn = _auctionMailDestination.Value;
            float distance = Distance(spawn.Position);
            if (distance < _mailClosest - 2) { _mailClosest = distance; _mailProgressAt = Now; }
            WorldObject target = _auctionMailbox.IsEmpty() ? null : Global.ObjAccessor.GetWorldObject(me, _auctionMailbox);
            target ??= FindLive(spawn.GameObject ? Array.Empty<int>() : new[] { spawn.Entry },
                spawn.GameObject ? new[] { spawn.Entry } : Array.Empty<int>(), _settings.TargetSearchRadius);
            SIBehaviorResult result = target == null ? Travel(P(spawn.Position), "Collect auction purchases", rememberFailure: false) :
                MoveWithin(target, 3.5f, "Collect auction purchases", npcInteraction: target is Creature);
            if (result != SIBehaviorResult.Failure && Now - _mailProgressAt < 45000)
            {
                if (target == null || result != SIBehaviorResult.Success) return true;
                _auctionMailbox = target.GetGUID();
                if (me.GetSession().CollectAuctionMailForSI(target, out int received))
                {
                    if (received > 0) { _nextEquipCheck = 0; EquipUpgrades(); NoteGameplayProgress(); _mailProgressAt = Now; }
                    if (HasAuctionMail()) return true;
                    Status = "Collected auction mail";
                    _auctionMailDestination = null; _awaitingPurchasedMail = false; CancelTravel(); return true;
                }
            }
            Status = "Auction mailbox unavailable; returning to previous activity";
            _auctionMailDestination = null; _awaitingPurchasedMail = false; _nextAuctionMailScan = Now + 60000;
            CancelTravel(); return true;
        }
    }
}
