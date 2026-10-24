using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.AI
{
    public static class SIQuestBatchPolicy
    {
        // Finish the current work area before returning. Once returning, finish the
        // completed turn-in batch before starting another area.
        public static SITask Choose(IEnumerable<SITask> candidates, Func<SITask, float> distance,
            Func<SITask, bool> inArea, bool returning)
        {
            var tasks = candidates.Where(task => task.Kind != SITaskKind.Grind).ToArray();
            var turnIns = tasks.Where(task => task.Kind == SITaskKind.TurnIn).OrderBy(distance).ToArray();
            var localReturn = turnIns.FirstOrDefault(inArea);
            var localWork = tasks.Where(task => task.Kind == SITaskKind.Objective && inArea(task)).OrderBy(distance).FirstOrDefault();
            var maintenance = tasks.Where(task => task.Kind == SITaskKind.Vendor).OrderBy(distance).FirstOrDefault();
            if (maintenance != null) return maintenance;
            if (returning && localReturn != null) return localReturn;
            return localWork ?? localReturn ?? turnIns.FirstOrDefault() ?? tasks.OrderBy(distance).FirstOrDefault();
        }

        // Gathering at our feet beats another optional pull. Beyond that small radius,
        // proximity decides which unfinished quest can use the next action.
        public static float NearbyCost(float distance, bool gathering) =>
            gathering && distance <= 8 ? distance - 40 : distance;
    }
}
