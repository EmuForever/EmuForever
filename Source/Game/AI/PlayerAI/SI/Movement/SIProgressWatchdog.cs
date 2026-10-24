using System;
using System.Numerics;
namespace Game.AI.SIMovement;

public enum SITravelProgress
{
    Started,
    Advancing,
    Waiting,
    Arrived,
    Stalled,
    Invalid
}

/// <summary>
/// Tracks actual displacement toward a stable task, rather than treating a
/// successful path request or a changed status string as gameplay progress.
/// Monotonic time is supplied by the World controller.
/// </summary>
public sealed class SIProgressWatchdog
{
    bool _tracking;
    int _map;
    Vector3 _destination;
    Vector3 _lastPosition;
    long _lastAdvancedAt;
    long _startedAt;
    long _maximumTaskAge;
    float _bestDistance;
    readonly long _stallMilliseconds;
    readonly float _minimumAdvance;

    public SIProgressWatchdog(long stallMilliseconds = 15000, float minimumAdvance = .75f)
    {
        if (stallMilliseconds < 1000) throw new ArgumentOutOfRangeException(nameof(stallMilliseconds));
        if (!float.IsFinite(minimumAdvance) || minimumAdvance <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumAdvance));
        _stallMilliseconds = stallMilliseconds;
        _minimumAdvance = minimumAdvance;
    }

    public long TaskAge(long now) => _tracking ? Math.Max(0, now - _startedAt) : 0;
    public long NoAdvanceAge(long now) => _tracking ? Math.Max(0, now - _lastAdvancedAt) : 0;

    public SITravelProgress Observe(int map, Vector3 position, Vector3 destination,
        long now, float arrivalDistance = 2, bool suspended = false)
    {
        if (map < 0 || !Finite(position) || !Finite(destination) ||
            !float.IsFinite(arrivalDistance) || arrivalDistance < 0)
            return SITravelProgress.Invalid;
        float distance = Distance(position, destination);
        if (distance <= arrivalDistance)
        {
            Reset();
            return SITravelProgress.Arrived;
        }
        if (!_tracking || _map != map || Distance(destination, _destination) > 6 || now < _lastAdvancedAt)
        {
            _tracking = true;
            _map = map;
            _destination = destination;
            _lastPosition = position;
            _bestDistance = distance;
            _startedAt = now;
            _maximumTaskAge = Math.Max(60000, (long)(distance / 2 * 1000) + 60000);
            _lastAdvancedAt = now;
            return SITravelProgress.Started;
        }
        if (suspended)
        {
            // Roots/casts and scripted pauses are not path failures. A resumed
            // task gets a full window to make measurable progress again.
            _lastAdvancedAt = now;
            _lastPosition = position;
            return SITravelProgress.Waiting;
        }
        float moved = Distance(position, _lastPosition);
        // A detour around a wall can temporarily increase straight-line distance.
        // Actual movement counts, but endless circling has a separate total-task
        // budget proportional to distance below.
        if (moved >= _minimumAdvance || _bestDistance - distance >= _minimumAdvance)
        {
            _lastPosition = position;
            _bestDistance = Math.Min(_bestDistance, distance);
            _lastAdvancedAt = now;
            if (now - _startedAt > _maximumTaskAge)
                return SITravelProgress.Stalled;
            return SITravelProgress.Advancing;
        }
        return now - _lastAdvancedAt >= _stallMilliseconds
            ? SITravelProgress.Stalled : SITravelProgress.Waiting;
    }

    public void Reset()
    {
        _tracking = false;
        _startedAt = 0;
        _lastAdvancedAt = 0;
        _bestDistance = 0;
    }

    static bool Finite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    static float Distance(Vector3 left, Vector3 right) =>
        (float)Math.Sqrt(Vector3.DistanceSquared(left, right));
}
