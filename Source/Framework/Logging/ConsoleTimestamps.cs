// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Framework.Logging
{
    public static class ConsoleTimestamps
    {
        static readonly object Sync = new();
        static bool _enabled;

        // Install before configuration/logging starts so direct console diagnostics are covered too.
        public static void Enable()
        {
            lock (Sync)
            {
                if (_enabled)
                    return;

                _view = ConsoleLogView.TryCreate();
                RestoreConsoleAfterFlush();
                _output = new TimestampWriter(Console.Out, Console.IsOutputRedirected);
                Console.SetOut(_output);
                Console.SetError(new TimestampWriter(Console.Error, Console.IsErrorRedirected));
                _enabled = true;
            }
        }

        // Console.ReadLine echoes its newline without going through Console.Out.
        public static string ReadLine()
        {
            if (_view != null && _view.TryReadLine(out string command))
                return command;
            string line = Console.ReadLine();
            if (!Console.IsInputRedirected)
            {
                lock (Sync)
                    _output?.BeginLine();
            }
            return line;
        }

        static TimestampWriter _output;
        static ConsoleLogView _view;

        public static void WritePrompt(string prompt)
        {
            if (_view == null || !_view.SetPrompt(prompt))
                Console.Write(prompt);
        }

        internal static bool WriteInteractive(ConsoleColor color, string text, bool newLine)
        {
            if (_view == null)
                return false;
            lock (Sync)
                return _view.Write((newLine ? text + Environment.NewLine : text).AsSpan(), color);
        }

        internal static void FlushInteractive() => _view?.Flush();

        // Re-register after the asynchronous appender's exit handler so its final messages render first.
        internal static void RestoreConsoleAfterFlush()
        {
            if (_view == null)
                return;
            AppDomain.CurrentDomain.ProcessExit -= RestoreConsole;
            AppDomain.CurrentDomain.ProcessExit += RestoreConsole;
        }

        static void RestoreConsole(object sender, EventArgs args)
        {
            _view?.Flush();
            _view?.Dispose();
        }

        sealed class TimestampWriter(TextWriter writer, bool redirected) : TextWriter
        {
            bool _lineStart = true;
            bool _carriageReturn;

            public void BeginLine()
            {
                _lineStart = true;
                _carriageReturn = false;
            }

            public override Encoding Encoding => writer.Encoding;
            public override string NewLine { get => writer.NewLine; set => writer.NewLine = value; }

            public override void Flush()
            {
                lock (Sync)
                {
                    _view?.Flush();
                    writer.Flush();
                }
            }

            public override void Write(char value) => Write(new ReadOnlySpan<char>(in value));
            public override void Write(string value) => Write(value.AsSpan());
            public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

            public override void Write(ReadOnlySpan<char> value)
            {
                lock (Sync)
                {
                    if (_view != null && _view.Write(value, Console.ForegroundColor))
                        return;
                    while (!value.IsEmpty)
                    {
                        // CRLF may arrive in separate writes. Never put a prefix between its two characters.
                        if (_carriageReturn && value[0] == '\n')
                        {
                            writer.Write('\n');
                            _carriageReturn = false;
                            value = value[1..];
                            continue;
                        }

                        if (_lineStart)
                            WriteTimestamp();

                        int end = value.IndexOfAny('\r', '\n');
                        if (end < 0)
                        {
                            writer.Write(value);
                            _lineStart = false;
                            _carriageReturn = false;
                            break;
                        }

                        writer.Write(value[..(end + 1)]);
                        _lineStart = true;
                        _carriageReturn = value[end] == '\r';
                        value = value[(end + 1)..];
                    }
                }
            }

            public override void WriteLine() => WriteLine(ReadOnlySpan<char>.Empty);
            public override void WriteLine(string value) => WriteLine(value.AsSpan());

            public override void WriteLine(ReadOnlySpan<char> value)
            {
                lock (Sync)
                {
                    Write(value);
                    Write(NewLine);
                }
            }

            void WriteTimestamp()
            {
                ConsoleColor? previousColor = null;
                try
                {
                    if (!redirected)
                    {
                        previousColor = Console.ForegroundColor;
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                    }
                }
                catch (IOException) { /* Redirected output still receives the timestamp. */ }

                try
                {
                    writer.Write("[" + DateTime.Now.ToString("hh:mm tt", CultureInfo.InvariantCulture) + "] ");
                }
                finally
                {
                    if (previousColor.HasValue)
                    {
                        try { Console.ForegroundColor = previousColor.Value; }
                        catch (IOException) { }
                    }
                }
            }
        }
    }
}
