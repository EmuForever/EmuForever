// TrinityCore wotlk_classic scripts ported to CypherCore. GPL version 2 or later; see LICENSE.
using Framework.Constants;
using Game.Entities;
using Game.Maps;
using Game.Scripting;

namespace Scripts.EasternKingdoms.Gnomeregan
{
    [Script]
    class instance_gnomeregan : InstanceMapScript
    {
        public instance_gnomeregan() : base(nameof(instance_gnomeregan), 90) { }
        public override InstanceScript GetInstanceScript(InstanceMap map) => new GnomereganInstance(map);
        class GnomereganInstance : InstanceScript
        {
            ObjectGuid _caveLeft, _caveRight, _blastmaster;
            public GnomereganInstance(InstanceMap map) : base(map)
            {
                SetHeaders("GNO");
                SetBossNumber(5);
                LoadDungeonEncounterData(new DungeonEncounterData[]
                {
                    new(0, 379), new(1, 378), new(2, 380), new(3, 381), new(4, 382)
                });
            }
            public override void OnCreatureCreate(Creature creature)
            {
                if (creature.GetEntry() == 7998) _blastmaster = creature.GetGUID();
            }
            public override void OnGameObjectCreate(GameObject go)
            {
                if (go.GetEntry() == 146085) _caveLeft = go.GetGUID();
                else if (go.GetEntry() == 146086) _caveRight = go.GetGUID();
            }
            public override void OnUnitDeath(Unit unit)
            {
                if (!unit.IsCreature()) return;
                int boss = unit.GetEntry() switch { 7079 => 1, 6235 => 2, 6229 => 3, 7800 => 4, _ => -1 };
                if (boss >= 0) SetBossState(boss, EncounterState.Done);
            }
            public override ObjectGuid GetGuidData(int type) => type switch
            {
                0 => _caveLeft,
                1 => _caveRight,
                2 => _blastmaster,
                _ => ObjectGuid.Empty
            };
        }
    }
}
