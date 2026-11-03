using Framework.Constants;
using Game.AI;
using Game.Entities;
using Game.Scripting;
using Game.Spells;
using System;

namespace Scripts.World;

// These are the script names already assigned to the Classic map's original objects.
[Script("go_classic_wsg_flag")]
[Script("go_classic_wsg_dropped_flag")]
[Script("go_classic_ab_banner")]
class go_classic_battleground_object : GameObjectAI
{
    public go_classic_battleground_object(GameObject obj) : base(obj) { }
    public override void Reset() => me.SetActive(true);
    public override bool OnGossipHello(Player player)
    {
        if (player.GetBattleground() is { } bg && player.CanUseBattlegroundObject(null))
            bg.EventPlayerClickedOnFlag(player, me);
        return true;
    }
}

[Script]
class go_classic_av_banner : GameObjectAI
{
    TimeSpan _remaining;
    public go_classic_av_banner(GameObject obj) : base(obj) { }
    public override void Reset()
    {
        me.SetActive(true);
        _remaining = me.GetEntry() is 178940 or 179435 or 179286 or 179287 ? TimeSpan.FromMinutes(4) : TimeSpan.Zero;
    }
    public override bool OnGossipHello(Player player)
    {
        if (player.CanUseBattlegroundObject(null) && player.GetBattleground() is { } bg && bg.GetStatus() == BattlegroundStatus.InProgress)
            bg.DoAction(1, player, me);
        return true;
    }
    public override void UpdateAI(TimeSpan diff)
    {
        if (me.GetZoneScript() is Game.BattleGrounds.Battleground bg && bg.GetStatus() != BattlegroundStatus.InProgress) return;
        if (_remaining <= TimeSpan.Zero) return;
        _remaining -= diff;
        if (_remaining <= TimeSpan.Zero) me.GetZoneScript()?.DoAction(2, me, me);
    }
}

[Script]
class spell_classic_wsg_flag_carrier : AuraScript
{
    void OnRemove(AuraEffect effect, AuraEffectHandleModes mode)
    {
        if (GetTarget().ToPlayer() is { } player && player.GetMapId() is 489 or 2996)
            player.GetBattleground()?.EventPlayerDroppedFlag(player);
    }
    public override void Register() => AfterEffectRemove.Add(new(OnRemove, 0, AuraType.EffectImmunity, AuraEffectHandleModes.Real));
}
