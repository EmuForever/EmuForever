using System;
using System.Collections.Generic;
using System.Linq;
namespace Game.AI;
public static class SIQuestOrderPolicy
{
    public static SITask Choose(IEnumerable<SITask> tasks,Func<SITask,long> acceptanceOrder,Func<SITask,float> distance,long primaryOrder=long.MinValue,float localRadius=150)
    {
        var ordered=tasks.Where(t=>t.Kind is SITaskKind.Objective or SITaskKind.TurnIn)
            .OrderBy(acceptanceOrder).ThenBy(distance).ToArray();
        if(ordered.Length==0)return null;
        var first=ordered[0];
        // The only exception to acceptance order is a later objective in the
        // current 150-yard neighborhood while the primary work is further away.
        if(distance(first)>localRadius)
            return ordered.FirstOrDefault(t=>t.Kind==SITaskKind.Objective&&distance(t)<=localRadius)??first;
        return first;
    }
}
