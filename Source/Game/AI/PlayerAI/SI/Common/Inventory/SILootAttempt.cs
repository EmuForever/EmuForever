using Game.Entities;

namespace Game.AI;

public sealed class SILootAttempt
{
    ObjectGuid _target;
    long _started;
    public const int Timeout = 20000;
    public bool Expired(ObjectGuid target, long now)
    {
        if (_target != target) { _target = target; _started = now; }
        return now - _started >= Timeout;
    }
}
