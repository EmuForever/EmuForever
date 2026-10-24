using System;

namespace Game.AI
{
    public sealed class SIInteractionExecution
    {
        long? _started;
        long _retryAt;
        int _failures;
        bool _done;
        long? _pausedAt;
        public void Pause(long now) => _pausedAt ??= now;
        public void Resume(long now)
        {
            if (_pausedAt is not long paused) return;
            long elapsed = Math.Max(0, now - paused);
            if (_started.HasValue) _started += elapsed;
            if (_retryAt != 0) _retryAt += elapsed;
            _pausedAt = null;
        }
        public void Reset() { _started = _pausedAt = null; _retryAt = 0; _failures = 0; _done = false; }
        public SIBehaviorResult Tick(Func<SIBehaviorResult> interact, long now)
        {
            Resume(now);
            if (_done) return SIBehaviorResult.Success;
            _started ??= now;
            if (now - _started.Value >= 20000 || _failures >= 3) return SIBehaviorResult.Failure;
            if (now < _retryAt) return SIBehaviorResult.Running;
            SIBehaviorResult result = interact();
            if (result == SIBehaviorResult.Success) { _done = true; return result; }
            if (result == SIBehaviorResult.Failure) { ++_failures; _retryAt = now + 2000; }
            return _failures >= 3 ? SIBehaviorResult.Failure : SIBehaviorResult.Running;
        }
    }
}
