using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Game.AI
{
    // SI fault diagnostics must not stop a map thread while console/file output
    // is blocked. The core's warning/fatal exception logging remains synchronous.
    internal sealed class SIBufferedLog : IDisposable
    {
        readonly BlockingCollection<string> _messages;
        readonly Action<string> _write;
        readonly Thread _worker;
        long _dropped, _unreportedDrops;
        int _closed;
        public long Dropped => Interlocked.Read(ref _dropped);

        public SIBufferedLog(Action<string> write, int capacity = 8192)
        {
            _write = write;
            _messages = new(capacity);
            _worker = new Thread(WriteMessages) { IsBackground = true, Name = "SI diagnostics" };
            _worker.Start();
        }
        public bool Publish(string message)
        {
            if (Volatile.Read(ref _closed) != 0) return false;
            try { if (_messages.TryAdd(message)) return true; }
            catch (InvalidOperationException) { return false; }
            Interlocked.Increment(ref _dropped); Interlocked.Increment(ref _unreportedDrops);
            return false;
        }
        void WriteMessages()
        {
            try
            {
                foreach (string message in _messages.GetConsumingEnumerable())
                    try
                    {
                        long dropped = Interlocked.Exchange(ref _unreportedDrops, 0);
                        if (dropped != 0) _write($"SI diagnostics: skipped {dropped} diagnostic messages while output was blocked.");
                        _write(message);
                    }
                    catch (Exception) { /* A closed output stream must not terminate worldserver. */ }
            }
            finally { _messages.Dispose(); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            _messages.CompleteAdding();
            _worker.Join(250);
        }
    }
}
