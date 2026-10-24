// TrinityCore wotlk_classic scripts ported to CypherCore. GPL version 2 or later; see LICENSE.
using Framework.Constants;
using Game.AI;
using Game.Entities;
using Game.Scripting;

namespace Scripts.Kalimdor.Winterspring
{
    [Script]
    class go_elune_fire : GameObjectAI
    {
        public go_elune_fire(GameObject go) : base(go) { }
        public override bool OnGossipHello(Player player)
        {
            if (me.FindNearestCreature(10300, 10)?.GetAI() is npc_ranshalla escort)
                escort.DoContinueEscort(me.GetEntry() == 177404);
            me.SetFlag(GameObjectFlags.NotSelectable);
            return false;
        }
    }
}
