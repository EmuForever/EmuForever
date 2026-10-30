using Game.Spells;

namespace Game.AI;

// Small combat decisions kept as pure functions so the controller self-test can check them.
public static class SISelfBuffPolicy
{
    public static bool Sustainable(long duration,long cooldown,int charges) =>
        (duration<0||duration>=300000) && cooldown==0 && charges==0;
    public static bool DuelBlocked(long now,long blockedSince) => now-blockedSince>=10000;
}

public static class SITravelCombatPolicy
{
    public const int LevelGap=5;
    public static bool Trivial(int playerLevel,int targetLevel,bool creature)=>creature&&playerLevel-targetLevel>=LevelGap;
    public static bool Pass(int playerLevel,int targetLevel,bool creature,bool travelling,bool deliberateFight,bool pvp)=>
        travelling&&!deliberateFight&&!pvp&&Trivial(playerLevel,targetLevel,creature);
}

public static class SICombatSpacing
{
    public static bool RangedAutoAttack(bool meleeStrategy, bool hunterMelee, bool rangedEquipped, bool inMeleeRange) =>
        !meleeStrategy && !hunterMelee && rangedEquipped && !inMeleeRange;

    public static bool StartMageRetreat(bool inCombat, float distance, bool rooted, float enemyHealthPct) =>
        inCombat && distance <= 8 && !rooted && enemyHealthPct >= 10;
}

public static class SIInstantAttackPolicy
{
    public static bool Prefer(int playerLevel,int targetLevel,bool creature)=>creature&&playerLevel-targetLevel>=30;
    public static bool Instant(SpellInfo spell)=>spell!=null&&!spell.IsChanneled()&&!spell.IsAutoRepeatRangedSpell()&&
        (spell.CastTimeEntry==null||spell.CastTimeEntry.Base<=0&&spell.CastTimeEntry.Minimum<=0);
}
