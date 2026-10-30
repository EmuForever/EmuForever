using System.Collections.Generic;
using System.Linq;
using Framework.Constants;
using Game.Entities;

namespace Game.AI;

public static class SITrainerCatalog
{
    // Class trainers in this world DB are attached to gossip menus, not the (0, 0) default.
    // Discovery and purchases must resolve the same validated creature_trainer relationships.
    public static IEnumerable<Trainer> ForCreature(int entry, Class playerClass = Class.None)
    {
        var template = Global.ObjectMgr.GetCreatureTemplate(entry);
        if (template == null || template.TrainerClass == Class.None ||
            playerClass != Class.None && template.TrainerClass != playerClass)
            return Enumerable.Empty<Trainer>();
        return Global.ObjectMgr.GetCreatureTrainerIds(entry).Select(Global.ObjectMgr.GetTrainer)
            .Where(trainer => trainer != null && trainer.GetTrainerType() is TrainerType.None or TrainerType.Talent);
    }
}

public static class SITrainerPolicy
{
    // Availability is data driven: every level, including level one and odd levels, is checked.
    public static bool NeedsQuery(int playerLevel, int queriedLevel) => playerLevel != queriedLevel;
}
