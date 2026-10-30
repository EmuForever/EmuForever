using System;
using System.Collections.Generic;
using System.Linq;
namespace Game.AI;
public static class SIQuestOrderPolicy
{
    // Work where the bot is before travelling anywhere. Inside the local radius: turn-ins first (the giver is right
    // here), then objectives, nearest first. With nothing local, the nearest remaining work, so a finished quest is
    // turned in when its giver is the closest thing left instead of costing a trip back after every single quest.
    // The current zone's work comes before other zones'; acceptance order only breaks ties.
    public static SITask Choose(IEnumerable<SITask> tasks,Func<SITask,long> acceptanceOrder,Func<SITask,float> distance,float localRadius=150,Func<SITask,bool> inZone=null)
    {
        var work=tasks.Where(t=>t.Kind is SITaskKind.Objective or SITaskKind.TurnIn).Select(t=>(Task:t,Distance:distance(t))).ToArray();
        if(work.Length==0)return null;
        var local=work.Where(w=>w.Distance<=localRadius).ToArray();
        if(local.Length!=0)
            return local.OrderBy(w=>w.Task.Kind==SITaskKind.TurnIn?0:1).ThenBy(w=>w.Distance).ThenBy(w=>acceptanceOrder(w.Task)).First().Task;
        if(inZone!=null&&work.Any(w=>inZone(w.Task)))work=work.Where(w=>inZone(w.Task)).ToArray();
        return work.OrderBy(w=>w.Distance).ThenBy(w=>acceptanceOrder(w.Task)).First().Task;
    }
}
