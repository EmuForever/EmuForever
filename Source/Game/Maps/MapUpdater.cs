// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Game.Entities;
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Channels;

namespace Game.Maps
{
    /// <summary>Bounded map dispatch. Each map has at most one queued or executing update.</summary>
    public sealed class MapUpdater
    {
        readonly Channel<MapUpdateRequest> _queue;
        readonly Thread[] _workerThreads;
        readonly object _lock = new();
        readonly HashSet<Map> _pendingMaps = new();
        readonly SemaphoreSlim _admission;
        readonly Action<Map, TimeSpan> _update;
        ExceptionDispatchInfo _failure;
        bool _stopping;
        int _pendingRequests;

        public MapUpdater(int numThreads, int capacity = 256, Action<Map, TimeSpan> update = null)
        {
            if (numThreads <= 0) throw new ArgumentOutOfRangeException(nameof(numThreads));
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            // Wait: map time/combat work must never be dropped. Only the coordinator
            // submits work; workers never synchronously enqueue into their own pool.
            _queue = Channel.CreateBounded<MapUpdateRequest>(new BoundedChannelOptions(capacity) {
                FullMode = BoundedChannelFullMode.Wait, SingleWriter = false,
                SingleReader = numThreads == 1, AllowSynchronousContinuations = false });
            _admission = new SemaphoreSlim(capacity + numThreads, capacity + numThreads);
            _update = update ?? ((map, diff) => map.Update(diff));
            _workerThreads = new Thread[numThreads];
            for (int i = 0; i < numThreads; ++i)
            {
                _workerThreads[i] = new Thread(WorkerThread) { Name = $"Map worker {i}" };
                _workerThreads[i].Start();
            }
        }

        public void ScheduleUpdate(Map map, TimeSpan diff)
        {
            ArgumentNullException.ThrowIfNull(map);
            // Bound admitted jobs as well as channel storage, including multiple producers.
            _admission.Wait();
            lock (_lock)
            {
                if (_stopping || !_pendingMaps.Add(map))
                {
                    _admission.Release();
                    throw new InvalidOperationException(_stopping ? "Map updater is stopping" : "Map update already pending");
                }
                ++_pendingRequests;
            }
            try { _queue.Writer.WriteAsync(new MapUpdateRequest(map, diff)).AsTask().GetAwaiter().GetResult(); }
            catch { UpdateFinished(map); throw; }
        }

        public void Wait()
        {
            ExceptionDispatchInfo failure;
            lock (_lock)
            {
                while (_pendingRequests != 0) Monitor.Wait(_lock);
                failure = _failure;
                _failure = null;
            }
            // Surface the first failure on the coordinator after all maps leave their
            // updates. Do not kill a worker and strand the global lifecycle barrier.
            failure?.Throw();
        }

        public void Deactivate()
        {
            lock (_lock) _stopping = true;
            try { Wait(); }
            finally
            {
                // Drain before completing; cancellation must not abandon pending counters.
                _queue.Writer.TryComplete();
                foreach (var thread in _workerThreads) thread.Join();
            }
        }

        void UpdateFinished(Map map)
        {
            lock (_lock)
            {
                _pendingMaps.Remove(map);
                --_pendingRequests;
                Monitor.PulseAll(_lock);
            }
            _admission.Release();
        }

        void WorkerThread()
        {
            while (_queue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (_queue.Reader.TryRead(out var request))
                {
                    try { _update(request.Map, request.Diff); }
                    catch (Exception error)
                    {
                        lock (_lock) _failure ??= ExceptionDispatchInfo.Capture(error);
                    }
                    finally { UpdateFinished(request.Map); }
                }
            }
        }
    }

    // Value work items avoid one managed request allocation per map per tick.
    public readonly record struct MapUpdateRequest(Map Map, TimeSpan Diff);
}
