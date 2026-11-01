// Copyright (c) EmuForever <http://github.com/EmuForever> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Constants;
using Game.AI;
using Game.Entities;
using System;
using System.Collections.Generic;

namespace Game.Movement
{
    class ChaseMovementGenerator : MovementGenerator
    {
        static Milliseconds RANGE_CHECK_INTERVAL = (Milliseconds)100; // time (ms) until we attempt to recalculate
        const float DirectChaseDistance = 15.0f; // yards within which a visible target is chased in a straight line when no path exists
        static readonly Milliseconds REPATH_INTERVAL = (Milliseconds)150; // minimum time between re-paths while already running toward a moving target
        static readonly Milliseconds SPREAD_CHECK_INTERVAL = (Milliseconds)400; // how often a settled melee attacker checks whether it overlaps another
        const float TargetMovedDistSq = 0.1f * 0.1f;
        const float SpreadSpacingFactor = 0.6f; // two attackers overlap when closer than this share of their summed combat reach
        const float MinSpreadSpacing = 1.0f;

        ChaseRange? _range;
        ChaseAngle? _angle;

        PathGenerator _path;
        Position _lastTargetPosition;
        TimeTracker _rangeCheckTimer;
        TimeTracker _repathTimer;
        TimeTracker _spreadTimer;
        bool _movingTowards = true;
        bool _mutualChase = true;

        AbstractFollower _abstractFollower;

        public ChaseMovementGenerator(Unit target, ChaseRange? range, ChaseAngle? angle)
        {
            _abstractFollower = new AbstractFollower(target);
            _range = range;
            _angle = angle;

            Mode = MovementGeneratorMode.Default;
            Priority = MovementGeneratorPriority.Normal;
            Flags = MovementGeneratorFlags.InitializationPending;
            BaseUnitState = UnitState.Chase;

            _rangeCheckTimer = new(RANGE_CHECK_INTERVAL);
            _repathTimer = new(REPATH_INTERVAL);
            _spreadTimer = new(SPREAD_CHECK_INTERVAL);
        }

        public override void Initialize(Unit owner)
        {
            RemoveFlag(MovementGeneratorFlags.InitializationPending | MovementGeneratorFlags.Deactivated);
            AddFlag(MovementGeneratorFlags.Initialized | MovementGeneratorFlags.InformEnabled);

            _path = null;
            _lastTargetPosition = null;
        }

        public override void Reset(Unit owner)
        {
            RemoveFlag(MovementGeneratorFlags.Deactivated);
            Initialize(owner);
        }

        public override bool Update(Unit owner, TimeSpan diff)
        {
            // owner might be dead or gone (can we even get nullptr here?)
            if (owner == null || !owner.IsAlive())
                return false;

            // our target might have gone away
            Unit target = _abstractFollower.GetTarget();
            if (target == null || !target.IsInWorld)
                return false;

            Creature cOwner = owner.ToCreature();

            // the owner might be unable to move (rooted or casting), or we have lost the target, pause movement
            if (owner.HasUnitState(UnitState.NotMove) || owner.IsMovementPreventedByCasting() || HasLostTarget(owner, target))
            {
                owner.StopMoving();
                _lastTargetPosition = null;
                if (cOwner != null)
                    cOwner.SetCannotReachTarget(false);
                return true;
            }

            bool mutualChase = IsMutualChase(owner, target);
            float hitboxSum = owner.GetCombatReach() + target.GetCombatReach();
            float minRange = _range.HasValue ? _range.Value.MinRange + hitboxSum : SharedConst.ContactDistance;
            float minTarget = (_range.HasValue ? _range.Value.MinTolerance : 0.0f) + hitboxSum;
            float maxRange = _range.HasValue ? _range.Value.MaxRange + hitboxSum : owner.GetMeleeRange(target); // melee range already includes hitboxes
            float maxTarget = _range.HasValue ? _range.Value.MaxTolerance + hitboxSum : SharedConst.ContactDistance + hitboxSum;
            ChaseAngle? angle = mutualChase ? null : _angle;

            if (cOwner != null && cOwner.IsIgnoringChaseRange())
                    minRange = minTarget = maxRange = maxTarget = 0.0f;

            // periodically check if we're already in the expected range...
            _rangeCheckTimer.Update(diff);
            bool rangeCheckTick = _rangeCheckTimer.Passed();
            if (rangeCheckTick)
            {
                _rangeCheckTimer.Reset(RANGE_CHECK_INTERVAL);
                if (HasFlag(MovementGeneratorFlags.InformEnabled) && PositionOkay(owner, target, _movingTowards ? null : minTarget, _movingTowards ? maxTarget : null, angle))
                {
                    RemoveFlag(MovementGeneratorFlags.InformEnabled);
                    _path = null;

                    if (cOwner != null)
                        cOwner.SetCannotReachTarget(false);

                    owner.StopMoving();
                    owner.SetInFront(target);
                    DoMovementInform(owner, target);
                    return true;
                }
            }

            // if we're done moving, we want to clean up
            if (owner.HasUnitState(UnitState.ChaseMove) && owner.MoveSpline.Finalized())
            {
                RemoveFlag(MovementGeneratorFlags.InformEnabled);
                _path = null;
                if (cOwner != null)
                    cOwner.SetCannotReachTarget(false);
                owner.ClearUnitState(UnitState.ChaseMove);
                owner.SetInFront(target);
                DoMovementInform(owner, target);
            }

            // Several melee attackers on one victim fan out around it instead of standing inside each other.
            _spreadTimer.Update(diff);
            if (_spreadTimer.Passed())
            {
                _spreadTimer.Reset(SPREAD_CHECK_INTERVAL);
                if (cOwner != null && !angle.HasValue && !mutualChase && TrySpreadOut(cOwner, target, maxTarget))
                {
                    _lastTargetPosition = new(target.GetPosition());
                    return true;
                }
            }

            // If the target moved, we have to consider whether to adjust. Position has no value equality, so compare
            // coordinates: a reference compare always differed and re-pathed every tick, even when the target only turned.
            // While already running a route, a moving target is re-pathed at most every REPATH_INTERVAL.
            _repathTimer.Update(diff);
            bool targetMoved = _lastTargetPosition == null || _lastTargetPosition.GetExactDistSq(target) > TargetMovedDistSq;
            if (targetMoved && owner.HasUnitState(UnitState.ChaseMove) && !owner.MoveSpline.Finalized() && !_repathTimer.Passed() && mutualChase == _mutualChase)
                targetMoved = false;
            // A standing target still needs a check now and then: knockback, fear or a script can move the creature itself.
            bool displaced = !targetMoved && rangeCheckTick && !owner.HasUnitState(UnitState.ChaseMove) &&
                !PositionOkay(owner, target, minRange, maxRange, angle);
            if (targetMoved || displaced || mutualChase != _mutualChase)
            {
                _repathTimer.Reset(REPATH_INTERVAL);
                _lastTargetPosition = new(target.GetPosition());
                _mutualChase = mutualChase;
                if (owner.HasUnitState(UnitState.ChaseMove) || !PositionOkay(owner, target, minRange, maxRange, angle))
                {
                    // can we get to the target?
                    if (cOwner != null && !target.IsInAccessiblePlaceFor(cOwner))
                    {
                        cOwner.SetCannotReachTarget(true);
                        cOwner.StopMoving();
                        cOwner.SetFacingToObject(target);
                        _path = null;
                        return true;
                    }

                    // figure out which way we want to move
                    bool moveToward = !owner.IsInDist(target, maxRange);

                    // make a new path if we have to...
                    if (_path == null || moveToward != _movingTowards)
                        _path = new PathGenerator(owner);

                    float x, y, z;
                    bool shortenPath;
                    // if we want to move toward the target and there's no fixed angle...
                    if (moveToward && !angle.HasValue)
                    {
                        // ...we'll pathfind to the center, then shorten the path
                        target.GetPosition(out x, out y, out z);
                        shortenPath = true;
                    }
                    else
                    {
                        // otherwise, we fall back to nearpoint finding
                        target.GetNearPoint(owner, out x, out y, out z, (moveToward ? maxTarget : minTarget) - hitboxSum, angle.HasValue ? target.ToAbsoluteAngle(angle.Value.RelativeAngle) : target.GetAbsoluteAngle(owner));
                        shortenPath = false;
                    }

                    if (owner.IsHovering())
                        owner.UpdateAllowedPositionZ(x, y, ref z);

                    bool success = _path.CalculatePath(x, y, z, owner.CanFly());
                    // A target close by and in sight is reachable even where the navmesh has a seam or a steep
                    // patch: run straight at it (the NoPath result already holds that shortcut) instead of
                    // freezing mid-turn and evading after CreatureNoPathEvadeTime.
                    bool directChase = success && _path.GetPathType().HasAnyFlag(PathType.NoPath) &&
                        owner.IsWithinDist(target, DirectChaseDistance) && owner.IsWithinLOSInMap(target);
                    if (!success || _path.GetPathType().HasAnyFlag(PathType.NoPath) && !directChase)
                    {
                        if (cOwner != null)
                            cOwner.SetCannotReachTarget(true);

                        owner.StopMoving();
                        owner.SetFacingToObject(target); // watch the target instead of freezing mid-turn
                        return true;
                    }

                    if (shortenPath)
                        _path.ShortenPathUntilDist(target, maxTarget);

                    if (cOwner != null)
                        cOwner.SetCannotReachTarget(false);

                    bool walk = false;
                    if (cOwner != null && !cOwner.IsPet())
                    {
                        switch (cOwner.GetMovementTemplate().GetChase())
                        {
                            case CreatureChaseMovementType.CanWalk:
                                walk = owner.IsWalking();
                                break;
                            case CreatureChaseMovementType.AlwaysWalk:
                                walk = true;
                                break;
                            default:
                                break;
                        }
                    }

                    owner.AddUnitState(UnitState.ChaseMove);
                    AddFlag(MovementGeneratorFlags.InformEnabled);

                    MoveSplineInit init = new(owner);
                    init.MovebyPath(_path.GetPath());
                    init.SetWalk(walk);
                    init.SetFacing(target);
                    init.Launch();
                }
            }

            // and then, finally, we're done for the tick
            return true;
        }

        public override void Deactivate(Unit owner)
        {
            AddFlag(MovementGeneratorFlags.Deactivated);
            RemoveFlag(MovementGeneratorFlags.Transitory | MovementGeneratorFlags.InformEnabled);
            owner.ClearUnitState(UnitState.ChaseMove);
            Creature cOwner = owner.ToCreature();
            if (cOwner != null)
                cOwner.SetCannotReachTarget(false);
        }

        public override void Finalize(Unit owner, bool active, bool movementInform)
        {
            AddFlag(MovementGeneratorFlags.Finalized);
            if (active)
            {
                owner.ClearUnitState(UnitState.ChaseMove);
                Creature cOwner = owner.ToCreature();
                if (cOwner != null)
                    cOwner.SetCannotReachTarget(false);
            }
        }

        public override MovementGeneratorType GetMovementGeneratorType() { return MovementGeneratorType.Chase; }

        public override void UnitSpeedChanged() { _lastTargetPosition = null; }

        static bool HasLostTarget(Unit owner, Unit target)
        {
            return owner.GetVictim() != target;
        }

        static bool IsMutualChase(Unit owner, Unit target)
        {
            if (target.GetMotionMaster().GetCurrentMovementGeneratorType() != MovementGeneratorType.Chase)
                return false;

            ChaseMovementGenerator movement = target.GetMotionMaster().GetCurrentMovementGenerator() as ChaseMovementGenerator;
            if (movement != null)
                return movement.GetTarget() == owner;

            return false;
        }

        static bool PositionOkay(Unit owner, Unit target, float? minDistance, float? maxDistance, ChaseAngle? angle)
        {
            float distSq = owner.GetExactDistSq(target);
            if (minDistance.HasValue && distSq < minDistance.Value * minDistance.Value)
                return false;
            if (maxDistance.HasValue && distSq > maxDistance.Value * maxDistance.Value)
                return false;
            if (angle.HasValue && !angle.Value.IsAngleOkay(target.GetRelativeAngle(owner)))
                return false;
            if (!owner.IsWithinLOSInMap(target))
                return false;
            return true;
        }

        // Retail-style melee separation. A settled attacker standing inside another attacker of the same victim steps
        // to the nearest free spot on the ring around the victim (closest angle first, so it only shuffles aside).
        // The attacker with the lower GUID keeps its spot, so a pair never both move; attackers already walking to
        // a spot are judged by their destination, so two mobs updated in the same tick do not pick the same gap.
        static bool TrySpreadOut(Creature owner, Unit target, float maxTarget)
        {
            if (owner.HasUnitState(UnitState.ChaseMove) || !owner.MoveSpline.Finalized() || owner.IsPet() || owner.IsGuardian())
                return false;
            if (target.IsMoving() || !target.MoveSpline.Finalized() || !owner.IsWithinMeleeRange(target))
                return false;

            var attackers = target.GetAttackers();
            if (attackers.Count < 2)
                return false;

            float reach = owner.GetCombatReach();
            float searchRadius = owner.GetExactDist2d(target) + 8.0f;
            List<(float X, float Y, float Spacing, bool Settled, long Order)> others = new();
            foreach (Unit other in attackers)
            {
                if (other == owner || !other.IsAlive() || other.IsPlayer() || other.GetExactDist2d(target) > searchRadius)
                    continue;
                float ox = other.GetPositionX(), oy = other.GetPositionY();
                if (!other.MoveSpline.Finalized())
                {
                    var destination = other.MoveSpline.FinalDestination();
                    ox = destination.X; oy = destination.Y;
                }
                others.Add((ox, oy, Math.Max(MinSpreadSpacing, (reach + other.GetCombatReach()) * SpreadSpacingFactor),
                    other.MoveSpline.Finalized(), other.GetGUID().GetCounter()));
            }

            bool Overlaps(float x, float y, bool onlyYielding)
            {
                foreach (var other in others)
                {
                    if (onlyYielding && other.Settled && other.Order > owner.GetGUID().GetCounter())
                        continue; // that one moves out of our way instead
                    float dx = x - other.X, dy = y - other.Y;
                    if (dx * dx + dy * dy < other.Spacing * other.Spacing)
                        return true;
                }
                return false;
            }

            if (!Overlaps(owner.GetPositionX(), owner.GetPositionY(), true))
                return false;

            // Stay on the current ring, inside melee reach.
            float minRadius = Math.Min(target.GetCombatReach() + reach * 0.5f, maxTarget - 0.25f);
            float radius = Math.Clamp(owner.GetExactDist2d(target), minRadius, maxTarget - 0.25f);
            float chord = Math.Min(1.0f, Math.Max(MinSpreadSpacing, reach * SpreadSpacingFactor) / (2.0f * radius));
            float step = Math.Max(0.15f, 2.0f * MathF.Asin(chord) * 0.5f);
            float baseAngle = target.GetAbsoluteAngle(owner);
            int steps = (int)MathF.Ceiling(MathF.PI / step);

            for (int k = 1; k <= steps; ++k)
            {
                foreach (int side in new[] { 1, -1 })
                {
                    float angle = baseAngle + side * k * step;
                    float x = target.GetPositionX() + radius * MathF.Cos(angle);
                    float y = target.GetPositionY() + radius * MathF.Sin(angle);
                    if (Overlaps(x, y, false))
                        continue;

                    float z = target.GetPositionZ();
                    owner.UpdateAllowedPositionZ(x, y, ref z);
                    if (Math.Abs(z - target.GetPositionZ()) > 3.0f || !target.IsWithinLOS(x, y, z))
                        continue;

                    PathGenerator path = new(owner);
                    if (!path.CalculatePath(x, y, z, owner.CanFly()) || path.GetPathType().HasAnyFlag(PathType.NoPath | PathType.Incomplete) ||
                        path.GetPathLength() > radius * 2.0f + 2.0f)
                        continue;

                    owner.AddUnitState(UnitState.ChaseMove);
                    MoveSplineInit init = new(owner);
                    init.MovebyPath(path.GetPath());
                    init.SetWalk(true);
                    init.SetFacing(target);
                    init.Launch();
                    return true;
                }
            }
            return false;
        }

        static void DoMovementInform(Unit owner, Unit target)
        {
            if (!owner.IsCreature())
                return;

            CreatureAI ai = owner.ToCreature().GetAI();
            if (ai != null)
                ai.MovementInform(MovementGeneratorType.Chase, (int)target.GetGUID().GetCounter());
        }

        public Unit GetTarget()
        {
            return _abstractFollower.GetTarget();
        }
    }
}
