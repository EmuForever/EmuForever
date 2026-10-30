using Framework.Constants;
using Game.Entities;
using System.Numerics;

namespace Game.AI;

public sealed partial class SIPlayer
{
    ObjectGuid _sightTarget;
    long _sightAt=-1;
    Vector3 _sightFrom,_sightTo;
    bool _combatSight;

    internal bool CombatSight(Unit target)
    {
        if(target==null||!me.IsInWorld||!me.IsInMap(target))return false;
        if(target==me)return true;
        var from=Here+new Vector3(0,0,me.GetCollisionHeight());
        var to=V(target.GetPosition())+new Vector3(0,0,target.GetCollisionHeight());
        if(_sightAt==Now&&_sightTarget==target.GetGUID()&&_sightFrom==from&&_sightTo==to)return _combatSight;
        _sightAt=Now;_sightTarget=target.GetGUID();_sightFrom=from;_sightTo=to;
        var terrain=Global.TerrainMgr.LoadTerrain(me.GetMapId());
        // Terrain reads also ensure the ray's grids/vmap tiles are loaded before
        // native LOS, which otherwise reports clear when no map tree is loaded.
        bool ground=terrain!=null&&SICombatSight.TerrainClear(from,to,(x,y)=>terrain.GetGridHeight(me.GetPhaseShift(),me.GetMapId(),x,y));
        return _combatSight=ground&&me.GetMap().IsInLineOfSight(me.GetPhaseShift(),from.X,from.Y,from.Z,to.X,to.Y,to.Z,
            LineOfSightChecks.All,ModelIgnoreFlags.Nothing);
    }

    void StopObstructedCombatCasts()
    {
        if(!me.IsInWorld||!me.IsAlive())return;
        foreach(var kind in new[]{CurrentSpellTypes.Generic,CurrentSpellTypes.Channeled})
        {
            var cast=me.GetCurrentSpell(kind);
            if(kind==CurrentSpellTypes.Generic && cast?.GetState()!=SpellState.Preparing)continue;
            var target=cast?.m_targets.GetUnitTarget();
            if(target==null||target==me||cast.GetSpellInfo().IsPositive()||cast.GetSpellInfo().HasAttribute(SpellAttr2.IgnoreLineOfSight))continue;
            if(!CombatSight(target))me.InterruptSpell(kind);
        }
    }
}
