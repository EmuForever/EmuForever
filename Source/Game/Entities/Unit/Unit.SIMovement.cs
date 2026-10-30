using Framework.Constants;
using Game.Movement;
using System;

namespace Game.Entities;

public partial class Unit
{
    // SI raw ground splines have an idle generator, which cannot propagate speed.
    public void RefreshSISplineSpeed(UnitMoveType? changedType = null)
    {
        var player=ToPlayer();
        if(player?.Controller==null || player.IsBeingTeleported() || MoveSpline.Finalized() || !MoveSpline.SpeedType.HasValue ||
            MoveSpline.onTransport || MoveSpline.splineflags.HasFlag(SplineFlag.Parabolic|SplineFlag.Falling|SplineFlag.Cyclic|SplineFlag.Animation|SplineFlag.Catmullrom) ||
            GetMotionMaster().GetCurrentMovementGeneratorType()!=MovementGeneratorType.Idle)return;
        var flags=GetUnitMovementFlags();
        flags=MoveSpline.Walking?flags|MovementFlag.Walking:flags&~MovementFlag.Walking;
        var speedType=MoveSplineInit.SelectSpeedType(flags);
        if(changedType.HasValue&&changedType.Value!=speedType)return;
        var speed=GetSpeed(speedType);
        if(MoveSpline.SpeedType==speedType&&Math.Abs(MoveSpline.velocity-speed)<.001f)return;
        var path=MoveSpline.RemainingPath();
        if(path.Length<2)return;
        int offset=MoveSpline.CurrentPathIdx();
        var move=new MoveSplineInit(this);
        move.MovebyPath(path);move.SetFirstPointId(offset);move.SetWalk(MoveSpline.Walking);
        move.SetUncompressed();move.SetSmoothGroundPath(false);move.SetVelocity(speed,speedType);
        move.Launch();
    }
}
