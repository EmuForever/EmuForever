using Game.DataStorage;
using System.Collections.Generic;
using System.Linq;

namespace Game.Systems.Legacy
{
    // IDs verified against the extracted SpellName/SpellEffect data, not assigned by the server.
    public enum LegacyPerkSpell
    {
        BountifulHarvest=1225435, WorkingOvertime=1225451, Luremaster=1225455, PerformanceBonus=1225456,
        MasterChef=1225457, Bartering=1225459, DedicatedStudy=1225469, ThrillOfAdventure=1225472,
        Talented=1225474, FieldMedicine=1225475, HighAlert=1225477, WellRested=1225478, FieldGuide=1225479,
        Diplomat=1225485, FrequentFlier=1225490, Reinforce=1225497, Gourmand=1225499,
        TheQuickAndTheDead=1225500, ForGreatHonor=1225501, Permanence=1225502, ReagentEconomy=1225503
    }
    public sealed record LegacyPerkDefinition(int NodeId, int EntryId, int DefinitionId, int SpellId, int MaximumRank);

    public static class LegacyDefinitions
    {
        // Verified Forever trees; tree 1118 is an earlier template, not one of the three client pages.
        public static bool IsTree(int id) => id is 1187 or 1188 or 1189;
        public static IReadOnlyList<LegacyPerkDefinition> Perks { get; private set; } = System.Array.Empty<LegacyPerkDefinition>();
        public static IReadOnlyList<int> PerkSpells { get; private set; } = System.Array.Empty<int>();
        public static void Load()
        {
            Perks = CliDB.TraitNodeXTraitNodeEntryStorage.Values
                .Where(link => CliDB.TraitNodeStorage.TryGetValue(link.TraitNodeID, out var node) && IsTree(node.TraitTreeID))
                .Select(link =>
                {
                    var entry = CliDB.TraitNodeEntryStorage[link.TraitNodeEntryID];
                    var definition = CliDB.TraitDefinitionStorage[entry.TraitDefinitionID];
                    return new LegacyPerkDefinition(link.TraitNodeID, entry.Id, definition.Id, definition.SpellID, entry.MaxRanks);
                }).Where(perk => System.Enum.IsDefined((LegacyPerkSpell)perk.SpellId)).ToArray();
            PerkSpells = Perks.Select(x => x.SpellId).Where(x => x != 0).Distinct().ToArray();
        }
    }
}
