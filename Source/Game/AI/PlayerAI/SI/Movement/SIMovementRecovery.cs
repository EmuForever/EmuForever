using Framework.Constants;
using Game.Entities;
using Game.Movement;
using System;
using System.Numerics;
namespace Game.AI;
public sealed partial class SIPlayer
{
    bool _recoveringMovement;
    int _movementRecoveryAttempts;
    int _movementRecoveryMap=-1;
    Vector3 _movementRecoveryOrigin;
    Vector3? _previousRecoveryStart;
    long _movementRecoveryUntil;
    bool TryMovementRecovery(string reason)
    {
        // Nudging the origin cannot repair a bad destination or connect missing
        // mesh corridors. Retrying those here caused endless back-and-forth steps.
        if(!reason.StartsWith("Movement blocked")&&!reason.Contains("no measurable progress")&&
            !reason.StartsWith("No walkable ground near the start"))return false;
        if(MovementSuspended||_goal=="flee"||!_hasMovementDestination)return false;
        if(_movementRecoveryMap!=me.GetMapId()||Vector3.Distance(Here,_movementRecoveryOrigin)>12)
        {_movementRecoveryMap=me.GetMapId();_movementRecoveryAttempts=0;_movementRecoveryOrigin=Here;_previousRecoveryStart=null;}
        if(_movementRecoveryAttempts>=3)return false;
        ++_movementRecoveryAttempts;
        var origin=Here;
        var forward=new Vector3(MathF.Cos(me.GetOrientation()),MathF.Sin(me.GetOrientation()),0);
        var left=new Vector3(-forward.Y,forward.X,0);
        var hazards=CaptureMovementThreats();
        // Rotate choices between attempts rather than repeating one blocked shove.
        Vector3[] directions=[-forward,left,-left,forward];
        for(int n=0;n<directions.Length;++n)
        {
            var end=origin+directions[(n+_movementRecoveryAttempts-1)%directions.Length]*2;
            // Do not immediately undo the previous nudge on the next failed leg.
            if(_previousRecoveryStart is Vector3 previousStart&&Vector3.DistanceSquared(end,previousStart)<2.25f)continue;
            bool entersThreat=false;foreach(var hazard in hazards)if(hazard.Blocks(origin,end,origin)){entersThreat=true;break;}
            if(entersThreat)continue;
            bool water=SwimmingSurface(origin)&&SwimmingSurface(end);
            if(!water)
            {
                float floor=me.GetMap().GetHeight(me.GetPhaseShift(),end.X,end.Y,origin.Z+1,true,3);
                if(!float.IsFinite(floor)||floor<-10000||Math.Abs(floor-origin.Z)>.7f)continue;
                end.Z=floor;
            }
            bool clear=true;var previous=origin;
            for(int step=1;step<=4&&clear;++step)
            {
                var point=Vector3.Lerp(origin,end,step/4f);
                if(!water)
                {
                    float floor=me.GetMap().GetHeight(me.GetPhaseShift(),point.X,point.Y,point.Z+1,true,3);
                    if(!float.IsFinite(floor)||floor<-10000||Math.Abs(floor-point.Z)>.5f){clear=false;break;}
                }
                clear=MovementSight(previous,point)&&me.GetMap().IsInLineOfSight(me.GetPhaseShift(),previous.X,previous.Y,previous.Z+1.8f,
                    point.X,point.Y,point.Z+1.8f,LineOfSightChecks.All,ModelIgnoreFlags.Nothing);
                previous=point;
            }
            if(!clear)continue;
            CancelAheadMovement();_pathCancel?.Cancel();_pathCancel?.Dispose();_pathCancel=null;_planning=null;_route=[];
            me.GetMotionMaster().Clear();me.StopMoving();
            var move=new MoveSplineInit(me);move.MovebyPath([Here,end]);move.SetUncompressed();move.SetWalk(true);
            if(move.Launch()<=0)continue;
            _previousRecoveryStart=origin;
            _recoveringMovement=true;_movementRecoveryUntil=Now+2500;
            SetNavigation("Taking a short recovery step",false);return true;
        }
        return false;
    }
}
