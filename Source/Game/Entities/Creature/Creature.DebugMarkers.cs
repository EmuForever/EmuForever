namespace Game.Entities;

public partial class Creature
{
    // Checked before owner/group/GM visibility shortcuts. Debug markers are
    // private to their operator and require actual GM mode, including on creation.
    internal bool IsPrivateGMMarker { get; set; }

    public override bool IsNeverVisibleFor(WorldObject seer, bool allowServersideObjects = false)
    {
        if(IsPrivateGMMarker && (seer.ToPlayer() is not { } player || !player.IsGameMaster() ||
            player.GetGUID()!=GetPrivateObjectOwner()))return true;
        return base.IsNeverVisibleFor(seer,allowServersideObjects);
    }
}
