using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Game.AI
{
    // Auction handlers normally execute on the world thread. Player AIs enqueue
    // intents here instead of mutating the shared AH / other players' mail on map workers.
    internal static class SIAuctionWork
    {
        static readonly ConcurrentQueue<Action> Work = new();
        static int _pending;
        internal static Task<T> Enqueue<T>(Func<T> action, T rejected)
        {
            if (Interlocked.Increment(ref _pending) > 256)
            { Interlocked.Decrement(ref _pending); return Task.FromResult(rejected); }
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Work.Enqueue(() =>
            {
                try { completion.TrySetResult(action()); }
                catch (Exception error) { completion.TrySetException(error); }
                finally { Interlocked.Decrement(ref _pending); }
            });
            return completion.Task;
        }
        internal static void Update()
        {
            for (int i = 0; i < 16 && Work.TryDequeue(out var action); ++i) action();
        }
    }
}
