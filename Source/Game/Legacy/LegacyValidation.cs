using Framework.Constants;
using Game.DataStorage;
using Game.Networking.Packets;
using System.Collections.Generic;

namespace Game.Systems.Legacy
{
    public static class LegacyValidation
    {
        // Called before walking costs/conditions: malformed nodes must never reach those lookups.
        public static bool EntriesBelongToConfig(TraitConfigPacket config)
        {
            HashSet<int> nodes = new();
            foreach (TraitEntryPacket entry in config.Entries)
            {
                TraitNodeRecord node = CliDB.TraitNodeStorage.LookupByKey(entry.TraitNodeID);
                TraitNodeEntryRecord definition = CliDB.TraitNodeEntryStorage.LookupByKey(entry.TraitNodeEntryID);
                if (node == null || definition == null || entry.Rank < 0 || entry.GrantedRanks < 0 || entry.BonusRanks != 0 ||
                    (long)entry.Rank + entry.GrantedRanks > definition.MaxRanks || !nodes.Add(entry.TraitNodeID)) return false;
                TraitTreeRecord tree = CliDB.TraitTreeStorage.LookupByKey(node.TraitTreeID);
                if (tree == null || (config.Type == TraitConfigType.Generic && tree.TraitSystemID != config.TraitSystemID)) return false;
                if (LegacyManager.IsLegacy(config) && !LegacyDefinitions.IsTree(tree.Id)) return false;
            }
            return true;
        }
    }
}
