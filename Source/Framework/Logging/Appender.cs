// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using Framework.Database;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

class ConsoleAppender : Appender
{
    public ConsoleAppender(byte id, string name, LogLevel level, AppenderFlags flags) : base(id, name, level, flags)
    {
        _consoleColor =
        [
            ConsoleColor.White,
            ConsoleColor.White,
            ConsoleColor.Gray,
            ConsoleColor.Green,
            ConsoleColor.Yellow,
            ConsoleColor.Red,
            ConsoleColor.Red,
            ConsoleColor.Magenta
        ];
    }

    // Console output is written by one background thread. A Windows console is slow, and it blocks every
    // writer outright while its window is paused (for example during a QuickEdit text selection); written
    // synchronously, that froze map threads for seconds at a time. Bursts beyond the queue are dropped and
    // counted rather than stalling the game.
    readonly record struct ConsoleEntry(ConsoleColor Color, LogLevel Level, LogFilter Type, string Prefix, string Text,
        int ThreadId, TaskCompletionSource<bool> Flushed = null);

    static readonly System.Collections.Concurrent.BlockingCollection<ConsoleEntry> Pending = new(20000);
    static long _dropped;
    static readonly System.Threading.Thread Writer = StartWriter();

    static System.Threading.Thread StartWriter()
    {
        var thread = new System.Threading.Thread(() =>
        {
            var formatter = new ServerLoadingConsoleFormatter(WriteConsole);
            foreach (var entry in Pending.GetConsumingEnumerable())
            {
                try
                {
                    if (entry.Flushed != null)
                    {
                        try
                        {
                            formatter.Flush();
                            Framework.Logging.ConsoleTimestamps.FlushInteractive();
                        }
                        finally { entry.Flushed.TrySetResult(true); }
                        continue;
                    }

                    long dropped = System.Threading.Interlocked.Exchange(ref _dropped, 0);
                    if (dropped != 0)
                        formatter.Write(LogLevel.Warn, LogFilter.Server, string.Empty,
                            $"Console output was too busy; skipped {dropped} log lines (log files keep them).", 0, ConsoleColor.Yellow);

                    formatter.Write(entry.Level, entry.Type, entry.Prefix, entry.Text, entry.ThreadId, entry.Color);
                }
                catch (Exception) { /* A closed console must not stop logging. */ }
            }
        }) { IsBackground = true, Name = "Console log" };
        thread.Start();
        // Let queued lines (a crash report, a shutdown message) reach the console before exit.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush();
        Framework.Logging.ConsoleTimestamps.RestoreConsoleAfterFlush();
        return thread;
    }

    public static void Flush()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        if (Pending.TryAdd(new ConsoleEntry(default, default, default, null, null, 0, completion), 2000))
            completion.Task.Wait(TimeSpan.FromMilliseconds(Math.Max(0, 2000 - watch.ElapsedMilliseconds)));
    }

    static void WriteConsole(ConsoleColor color, string text, bool newLine)
    {
        if (Framework.Logging.ConsoleTimestamps.WriteInteractive(color, text, newLine))
            return;

        try { Console.ForegroundColor = color; }
        catch (IOException) { /* Redirected output can still receive the text. */ }

        if (newLine)
            Console.WriteLine(text);
        else
            Console.Write(text);

        try { Console.ResetColor(); }
        catch (IOException) { }
    }

    public override void _write(LogMessage message)
    {
        _ = Writer;
        // Snapshot mutable prefixes before another appender applies its own formatting.
        if (!Pending.TryAdd(new ConsoleEntry(_consoleColor[(int)message.level], message.level, message.type,
            message.prefix, message.text, message.ThreadId)))
            System.Threading.Interlocked.Increment(ref _dropped);
    }

    public override AppenderType GetAppenderType()
    {
        return AppenderType.Console;
    }

    ConsoleColor[] _consoleColor;
}

class FileAppender : Appender, IDisposable
{
    public FileAppender(byte id, string name, LogLevel level, string fileName, string logDir, AppenderFlags flags) : base(id, name, level, flags)
    {
        Directory.CreateDirectory(logDir);
        _fileName = fileName;
        _logDir = logDir;
        _dynamicName = _fileName.Contains("{0}");

        if (_dynamicName)
        {
            Directory.CreateDirectory(logDir + "/" + _fileName.Substring(0, _fileName.IndexOf('/') + 1));
            return;
        }

        _logStream = OpenFile(_fileName, FileMode.Create);
    }

    FileStream OpenFile(string filename, FileMode mode)
    {
        return new FileStream(_logDir + "/" + filename, mode, FileAccess.Write, FileShare.ReadWrite);
    }

    public override void _write(LogMessage message)
    {
        lock (locker)
        {
            var logBytes = Encoding.UTF8.GetBytes(message.prefix + message.text + "\r\n");

            if (_dynamicName)
            {
                var logStream = OpenFile(string.Format(_fileName, message.dynamicName), FileMode.Append);
                logStream.Write(logBytes, 0, logBytes.Length);
                logStream.Flush();
                logStream.Close();
                return;
            }

            _logStream.Write(logBytes, 0, logBytes.Length);
            _logStream.Flush();
        }
    }

    public override AppenderType GetAppenderType()
    {
        return AppenderType.File;
    }

    #region IDisposable Support
    private bool disposedValue;

    private void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                _logStream.Dispose();
            }

            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }
    #endregion

    string _fileName;
    string _logDir;
    bool _dynamicName;
    FileStream _logStream;
    object locker = new();
}

class DBAppender : Appender
{
    public DBAppender(byte id, string name, LogLevel level) : base(id, name, level) { }

    public override void _write(LogMessage message)
    {
        // Avoid infinite loop, PExecute triggers Logging with "sql.sql" type
        if (!enabled || message.type == LogFilter.Sql)
            return;

        PreparedStatement stmt = LoginDatabase.GetPreparedStatement(LoginStatements.INS_LOG);
        stmt.SetInt64(0, (UnixTime64)message.mtime);
        stmt.SetInt32(1, realmId);
        stmt.SetString(2, message.type.ToString());
        stmt.SetUInt8(3, (byte)message.level);
        stmt.SetString(4, message.text);
        DB.Login.Execute(stmt);
    }

    public override AppenderType GetAppenderType()
    {
        return AppenderType.DB;
    }

    public override void setRealmId(int _realmId)
    {
        enabled = true;
        realmId = _realmId;
    }

    int realmId;
    bool enabled;
}

abstract class Appender
{
    protected Appender(byte id, string name, LogLevel level = LogLevel.Disabled, AppenderFlags flags = AppenderFlags.None)
    {
        _id = id;
        _name = name;
        _level = level;
        _flags = flags;
    }

    public void Write(LogMessage message)
    {
        if (_level == LogLevel.Disabled || (_level != LogLevel.Fatal && _level > message.level))
            return;

        StringBuilder ss = new();

        if (_flags.HasAnyFlag(AppenderFlags.PrefixTimestamp))
            ss.AppendFormat($"{message.mtime:MM/dd/yyyy HH:mm:ss}");

        if (_flags.HasAnyFlag(AppenderFlags.PrefixLogLevel))
            ss.AppendFormat($"{message.level}: ");

        if (_flags.HasAnyFlag(AppenderFlags.PrefixLogFilterType))
            ss.AppendFormat($"[{message.type}] ");

        message.prefix = ss.ToString();
        _write(message);
    }

    public abstract void _write(LogMessage message);

    public byte getId()
    {
        return _id;
    }

    public string getName()
    {
        return _name;
    }

    public abstract AppenderType GetAppenderType();

    public virtual void setRealmId(int realmId) { }

    public void setLogLevel(LogLevel level)
    {
        _level = level;
    }

    byte _id;
    string _name;
    LogLevel _level;
    AppenderFlags _flags;
}

class LogMessage
{
    public LogMessage(LogLevel _level, LogFilter _type, string _text)
    {
        level = _level;
        type = _type;
        text = _text;
        mtime = DateTime.Now;
        ThreadId = Environment.CurrentManagedThreadId;
    }

    public LogLevel level;
    public LogFilter type;
    public string text;
    public string prefix;
    public string dynamicName;
    public DateTime mtime;
    public int ThreadId { get; }
}
