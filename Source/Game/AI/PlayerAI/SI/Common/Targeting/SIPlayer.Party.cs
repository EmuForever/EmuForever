using Game.Entities;
using System;
using System.Linq;

namespace Game.AI
{
    public sealed partial class SIPlayer
    {
        readonly SIPartyPolicy _partyPolicy = new();
        Player _partyAnchor;
        Player[] _partyMembers = Array.Empty<Player>();
        bool _partyMoving;
        bool PartyFollowing => _partyAnchor != null && _goal is "quest" or "grind";
        bool PartyErrand => OwnedServiceTrip || !_flightMaster.IsEmpty() ||
            _auctionMailDestination.HasValue || _awaitingPurchasedMail;

        void RefreshPartyContext()
        {
            var group = me.GetGroup();
            Player previous = _partyAnchor;
            if (group == null)
            {
                _partyAnchor = null; _partyMembers = Array.Empty<Player>();
            }
            else
            {
                var members = new System.Collections.Generic.List<Player>();
                for (var member = group.GetFirstMember(); member != null; member = member.Next())
                {
                    Player player = member.GetSource();
                    if (player != null && player != me && player.IsInWorld && !player.GetSession().PlayerDisconnected()) members.Add(player);
                }
                _partyMembers = members.ToArray();
                var candidates = _partyMembers.Where(player => player.IsAlive()).ToArray();
                if (candidates.Length == 0) candidates = _partyMembers;
                var local = candidates.Where(player => player.GetMap() == me.GetMap() && me.InSamePhase(player)).ToArray();
                _partyAnchor = local.FirstOrDefault(player => player.GetGUID() == group.GetLeaderGUID());
                // An SI leader leads an all-bot party. When a real player is present,
                // stay with that player instead of taking their group on solo errands.
                _partyAnchor ??= local.Where(player => !player.GetSession().IsSIControlled)
                    .OrderBy(player => player.GetGUID().GetCounter()).FirstOrDefault();
                if (_partyAnchor == null && group.GetLeaderGUID() != me.GetGUID())
                    _partyAnchor = local.OrderBy(player => player.GetGUID().GetCounter()).FirstOrDefault();
                _partyAnchor ??= candidates.FirstOrDefault(player => player.GetGUID() == group.GetLeaderGUID());
                _partyAnchor ??= candidates.Where(player => !player.GetSession().IsSIControlled)
                    .OrderBy(player => player.GetGUID().GetCounter()).FirstOrDefault();
                if (_partyAnchor == null && group.GetLeaderGUID() != me.GetGUID())
                    _partyAnchor = candidates.OrderBy(player => player.GetGUID().GetCounter()).FirstOrDefault();
            }
            if (previous != _partyAnchor && (_partyMoving || !PartyErrand && _goal is "quest" or "grind"))
            { CancelTravel(); _partyMoving = false; }
            if (!PartyFollowing) _partyPolicy.Evaluate(false, false, 0);
            else if (PartyErrand) _partyPolicy.BeginErrand();
        }

        void MarkPartyErrand()
        {
            if (PartyFollowing) _partyPolicy.BeginErrand();
        }

        bool TickPartyCohesion(bool regroupOnly = false)
        {
            bool reachable = PartyFollowing && _partyAnchor.GetMap() == me.GetMap() && me.InSamePhase(_partyAnchor);
            float distance = reachable ? me.GetExactDist(_partyAnchor) : float.PositiveInfinity;
            SIPartyAction action = _partyPolicy.Evaluate(PartyFollowing, PartyErrand, distance);
            if (action is SIPartyAction.Solo or SIPartyAction.Errand || regroupOnly && action == SIPartyAction.Stay) return false;
            _task?.Suspend(Now);
            if (action == SIPartyAction.Wait)
            {
                if (_partyMoving) CancelTravel();
                _partyMoving = false;
                Status = "Waiting for party members on this map/in this phase";
            }
            else if (action == SIPartyAction.Regroup || _partyAnchor.IsMoving() && distance > SIPartyPolicy.FollowDistance)
            {
                _partyMoving = true;
                Status = (_partyPolicy.Returning ? "Returning to party near " : "Staying with party near ") + _partyAnchor.GetName();
                if (Travel(_partyAnchor.GetPosition(), Status, SIPartyPolicy.FollowDistance, destinationTolerance: 3, within3D: true) == SIBehaviorResult.Failure)
                    Status = "Party route is blocked; waiting to retry near " + _partyAnchor.GetName();
            }
            else
            {
                if (_partyMoving) CancelTravel();
                _partyMoving = false;
                Status = "Staying with party near " + _partyAnchor.GetName();
            }
            Behavior = "Party";
            return true;
        }

        bool PartyTargetAllowed(Unit target) => !PartyFollowing || PartyErrand || target != null &&
            target.GetMap() == _partyAnchor.GetMap() && target.IsWithinDistInMap(_partyAnchor, SIPartyPolicy.AssistRadius);

        Unit PartyAssistTarget()
        {
            if (!PartyFollowing || PartyErrand || _partyAnchor.GetMap() != me.GetMap()) return null;
            foreach (Player member in new[] { _partyAnchor }.Concat(_partyMembers).Distinct())
            {
                if (member.GetMap() != me.GetMap() || !me.InSamePhase(member)) continue;
                Unit target = member.GetVictim();
                if (CanFight(target) && member.IsInCombatWith(target) && PartyTargetAllowed(target) && !HeldByMagePolymorph(target)) return target;
                target = member.GetAttackers().FirstOrDefault(attacker => CanFight(attacker) && PartyTargetAllowed(attacker) && !HeldByMagePolymorph(attacker));
                if (target != null) return target;
            }
            return null;
        }
    }
}
