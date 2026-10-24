// TrinityCore wotlk_classic scripts ported to CypherCore. GPL version 2 or later; see LICENSE.
using Game.Entities;
using Game.Maps;
using Game.Scripting;

namespace Scripts.EasternKingdoms.ScarletMonastery
{
    static class ScarletMonasteryData
    {
        public const int DATA_INTERROGATOR_VISHAS = 0, DATA_BLOODMAGE_THALNOS = 1,
            DATA_HOUNDMASTER_LOKSEY = 2, DATA_ARCANIST_DOAN = 3, DATA_HEROD = 4,
            DATA_HIGH_INQUISITOR_FAIRBANKS = 5, DATA_MOGRAINE_AND_WHITE_EVENT = 6,
            DATA_AZSHIR = 7, DATA_SCORN = 8, DATA_MOGRAINE = 9, DATA_VORREL = 10,
            DATA_WHITEMANE = 11, DATA_HIGH_INQUISITORS_DOOR = 15, NPC_MOGRAINE = 3976;
    }

    [Script]
    class instance_scarlet_monastery : InstanceMapScript
    {
        public instance_scarlet_monastery() : base(nameof(instance_scarlet_monastery), 189) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new ScarletMonasteryInstance(map);
        class ScarletMonasteryInstance : InstanceScript
        {
            public ScarletMonasteryInstance(InstanceMap map) : base(map)
            {
                SetHeaders("SM");
                SetBossNumber(9);
                LoadObjectData(new ObjectData[] { new(3976, 9), new(3981, 10), new(3977, 11) },
                    new ObjectData[] { new(104600, 15) });
                LoadDungeonEncounterData(new DungeonEncounterData[]
                {
                    new(0, 444), new(1, 2818), new(2, 446), new(3, 447),
                    new(4, 448), new(5, 449), new(6, 450)
                });
            }
        }
    }
}
