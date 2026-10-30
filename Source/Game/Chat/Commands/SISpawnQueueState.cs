namespace Game.Chat.Commands
{
    public enum SISpawnSessionAction { Spawn, WaitForLogout, Skip }

    // Starting another spawn request never cancels earlier work. Only an explicit
    // despawn-all advances the cancellation generation.
    public sealed class SISpawnQueueState
    {
        readonly object _lock = new();
        long _generation;
        long? _activeAll;
        public long Generation { get { lock (_lock) return _generation; } }
        public bool TryBeginAll(out long generation)
        {
            lock (_lock)
            {
                generation = _generation;
                if (_activeAll == generation) return false;
                _activeAll = generation; return true;
            }
        }
        public bool IsCurrent(long generation) { lock (_lock) return _generation == generation; }
        public void CompleteAll(long generation) { lock (_lock) if (_activeAll == generation) _activeAll = null; }
        public void CancelAll() { lock (_lock) { ++_generation; _activeAll = null; } }
        public static SISpawnSessionAction SessionAction(bool hasSession, bool headless, bool disconnecting, bool withinLogoutWait) =>
            !hasSession ? SISpawnSessionAction.Spawn : headless && disconnecting && withinLogoutWait
                ? SISpawnSessionAction.WaitForLogout : SISpawnSessionAction.Skip;
    }
}
