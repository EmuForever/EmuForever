namespace Game.Maps
{
    // Exact per-thread accumulators for the once-a-minute map tick breakdown. The sampling profiler
    // only stops threads at GC poll points and misattributes time; these are plain timers.
    public static class MapTickProfile
    {
        [System.ThreadStatic] public static double BotAiMs;
        [System.ThreadStatic] public static double CreatureMs;
        [System.ThreadStatic] public static double GameObjectMs;
    }
}
