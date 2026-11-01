using Game.Entities;
using System;
using System.Linq;

namespace Game.AI;

public sealed partial class SIPlayer
{
    Player[] _partyMembers = Array.Empty<Player>();

    // Membership supports buffs and assistance, without restricting movement.
    void RefreshPartyContext()
    {
        var group = me.GetGroup();
        if (group == null) { _partyMembers = Array.Empty<Player>(); return; }
        var members = new System.Collections.Generic.List<Player>();
        for (var member = group.GetFirstMember(); member != null; member = member.Next())
        {
            var player = member.GetSource();
            if (player != null && player != me && player.IsInWorld && !player.GetSession().PlayerDisconnected())
                members.Add(player);
        }
        _partyMembers = members.ToArray();
    }

    Unit PartyAssistTarget()
    {
        if (me.InBattleground()) return null; // Battleground objectives own PvP target selection.
        foreach (var member in _partyMembers)
        {
            if (member.GetMap() != me.GetMap() || !me.InSamePhase(member)) continue;
            var target = member.GetVictim();
            if (CanFight(target) && member.IsInCombatWith(target) && !HeldByMagePolymorph(target)) return target;
            target = member.GetAttackers().FirstOrDefault(attacker => CanFight(attacker) && !HeldByMagePolymorph(attacker));
            if (target != null) return target;
        }
        return null;
    }
}
