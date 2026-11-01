using Framework.Constants;
using Game.BattleGrounds;
using System;

namespace Game.AI;

internal enum SIWarsongAssignment { Fetch, Escort, Recover }

internal static class SIBattlegroundPolicy
{
    internal static string GoalForLevel(string goal,int level) => goal=="bg"&&level<10?"quest":goal;
    internal static SIWarsongAssignment WarsongAssignment(int memberIndex, int availablePlayers,
        bool friendlyCarrier, bool ownFlagMissing) =>
        ownFlagMissing && memberIndex < Math.Max(1, (availablePlayers + 1) / 2) ? SIWarsongAssignment.Recover :
        friendlyCarrier ? SIWarsongAssignment.Escort : SIWarsongAssignment.Fetch;

    internal static bool Supported(BattlegroundTypeId type) => type is BattlegroundTypeId.AV or BattlegroundTypeId.WS or
        BattlegroundTypeId.WgCtf or BattlegroundTypeId.AB or BattlegroundTypeId.DomAb or BattlegroundTypeId.DI;

    internal static float Score(int priority, float distance, int nearbyAllies, bool retained, long player, string key)
    {
        // Stable variation and congestion spread bots among useful objectives.
        uint hash = unchecked((uint)player * 2654435761);
        foreach (char c in key) hash = unchecked(hash * 31 + c);
        if (priority is 35 or 40 && player % 5 == 0) priority = 10;
        return priority * 100 + distance + Math.Max(0, nearbyAllies - 1) * 45 + hash % 70 - (retained ? 80 : 0);
    }

    internal static bool CanMount(bool carryingFlag, BattlegroundStatus status) => !carryingFlag && status == BattlegroundStatus.InProgress;

}
