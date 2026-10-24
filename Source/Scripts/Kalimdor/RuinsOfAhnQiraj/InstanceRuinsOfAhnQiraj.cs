// TrinityCore wotlk_classic scripts ported to CypherCore. GPL version 2 or later; see LICENSE.
using Game.Entities;
using Game.Maps;
using Game.Scripting;

namespace Scripts.Kalimdor.RuinsOfAhnQiraj
{
    [Script]
    class instance_ruins_of_ahnqiraj : InstanceMapScript
    {
        public instance_ruins_of_ahnqiraj() : base(nameof(instance_ruins_of_ahnqiraj), 509) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new RuinsInstance(map);
        class RuinsInstance : InstanceScript
        {
            readonly ObjectGuid[] _bosses = new ObjectGuid[6];
            ObjectGuid _paralyzed;
            public RuinsInstance(InstanceMap map) : base(map)
            {
                SetHeaders("AQR");
                SetBossNumber(6);
                LoadDungeonEncounterData(new DungeonEncounterData[]
                {
                    new(0,718), new(1,719), new(2,720), new(3,721), new(4,722), new(5,723)
                });
            }
            public override void OnCreatureCreate(Creature creature)
            {
                int boss = creature.GetEntry() switch { 15348 => 0, 15341 => 1, 15340 => 2, 15370 => 3, 15369 => 4, 15339 => 5, _ => -1 };
                if (boss >= 0) _bosses[boss] = creature.GetGUID();
            }
            public override void SetGuidData(int type, ObjectGuid data) { if (type == 7) _paralyzed = data; }
            public override ObjectGuid GetGuidData(int type) => type >= 0 && type < 6 ? _bosses[type] : type == 7 ? _paralyzed : ObjectGuid.Empty;
        }
    }
}
