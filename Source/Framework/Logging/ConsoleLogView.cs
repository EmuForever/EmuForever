// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Framework.Logging
{
    // The input pump is the only console-input reader while this view is active. Mouse clicks and
    // command entry must share it; Console.ReadLine would otherwise discard mouse input records.
    sealed partial class ConsoleLogView : IDisposable
    {
        readonly object _sync = new();
        readonly ConsoleLogBuffer _buffer = new();
        readonly BlockingCollection<string> _commands = new();
        readonly StringBuilder _input = new();
        readonly List<string> _history = new();
        readonly ITerminal _terminal;
        readonly Timer _timer;
        List<ConsoleLogRow> _rows = new();
        ConsoleLogRow[] _visible = Array.Empty<ConsoleLogRow>();
        SmallRect _window;
        long _version = -1;
        int _width;
        int _top;
        int _logHeight;
        int _caret;
        int _historyIndex;
        bool _follow = true;
        bool _dirty = true;
        bool _leftDown;
        volatile bool _active = true;
        string _prompt;
        string _draft = "";

        internal ConsoleLogView(ITerminal terminal)
        {
            _terminal = terminal;
            _timer = new Timer(_ => Render(), null, Timeout.Infinite, 100);
            _timer.Change(0, 100);
            new Thread(ReadInput) { IsBackground = true, Name = "Console mouse and commands" }.Start();
        }

        public static ConsoleLogView TryCreate()
        {
            if (Console.IsOutputRedirected || Console.IsErrorRedirected || Console.IsInputRedirected
                || Environment.GetEnvironmentVariable("CYPHER_CONSOLE_INTERACTIVE") == "0")
                return null;

            ITerminal terminal = null;
            try
            {
                terminal = OperatingSystem.IsWindows() ? WindowsTerminal.TryCreate()
                    : OperatingSystem.IsLinux() ? LinuxTerminal.TryCreate() : null;
                return terminal == null ? null : new ConsoleLogView(terminal);
            }
            catch
            {
                terminal?.Dispose();
                return null;
            }
        }

        public bool Write(ReadOnlySpan<char> text, ConsoleColor color)
        {
            lock (_sync)
            {
                if (!_active)
                    return false;
                _buffer.Write(text, color);
                _dirty = true;
                return true;
            }
        }

        public bool SetPrompt(string prompt)
        {
            lock (_sync)
            {
                if (!_active)
                    return false;
                _prompt = prompt;
                _dirty = true;
                return true;
            }
        }

        public bool TryReadLine(out string line)
        {
            try
            {
                line = _commands.Take();
                return true;
            }
            catch (InvalidOperationException)
            {
                line = null;
                return false;
            }
        }

        public void Flush() => Render();

        void Render()
        {
            lock (_sync)
            {
                if (!_active)
                    return;
                try
                {
                    if (!_terminal.GetScreen(out var info))
                    {
                        Dispose();
                        return;
                    }
                    int width = info.Window.Right - info.Window.Left + 1;
                    int height = info.Window.Bottom - info.Window.Top + 1;
                    if (!_dirty && _window.Equals(info.Window))
                        return;

                    _logHeight = Math.Max(1, height - (_prompt == null ? 1 : 2));
                    if (_width != width || _version != _buffer.Version)
                    {
                        ConsoleLogRow? anchor = !_follow && _top < _rows.Count ? _rows[_top] : null;
                        _rows = _buffer.Layout(width);
                        if (anchor.HasValue)
                        {
                            int found = _rows.FindIndex(row => row.Line == anchor.Value.Line
                                && row.Offset <= anchor.Value.Offset && row.Offset + Math.Max(1, row.Count) > anchor.Value.Offset);
                            _top = found >= 0 ? found : 0;
                        }
                        _width = width;
                        _version = _buffer.Version;
                    }

                    int bottom = Math.Max(0, _rows.Count - _logHeight);
                    _top = _follow ? bottom : Math.Clamp(_top, 0, bottom);
                    var cells = new CharInfo[width * height];
                    ushort background = (ushort)(info.Attributes & 0xF0);
                    for (int i = 0; i < cells.Length; ++i)
                        cells[i] = new CharInfo(' ', (ushort)(background | (ushort)ConsoleColor.Gray));

                    int count = Math.Min(_logHeight, _rows.Count - _top);
                    _visible = new ConsoleLogRow[count];
                    for (int y = 0; y < count; ++y)
                    {
                        var row = _rows[_top + y];
                        _visible[y] = row;
                        if (row.HasTimestamp)
                            Put(cells, width, y, 0, row.Line.Timestamp, ConsoleColor.DarkGray, background);
                        for (int x = 0; x < row.Count && ConsoleLogLine.PrefixLength + x < width; ++x)
                        {
                            int offset = row.Offset + x;
                            cells[y * width + ConsoleLogLine.PrefixLength + x] = new CharInfo(row.Line.CharacterAt(offset),
                                (ushort)(background | (ushort)row.Line.ColorAt(offset)));
                        }
                    }

                    string status = "Click timestamp: expand/collapse | Wheel/PgUp/PgDn: scroll | Ctrl+End: follow";
                    if (!_follow)
                        status = "[History] " + status;
                    Put(cells, width, height - 1, 0, status, ConsoleColor.DarkGray, background);

                    int cursorX = 0;
                    if (_prompt != null && height >= 2)
                    {
                        int prefix = Math.Min(_prompt.Length, Math.Max(0, width - 2));
                        int capacity = Math.Max(1, width - prefix - 1);
                        int start = Math.Max(0, _caret - capacity);
                        Put(cells, width, height - 2, 0, _prompt[..prefix], ConsoleColor.Green, background);
                        Put(cells, width, height - 2, prefix, _input.ToString(start, _input.Length - start), ConsoleColor.Green, background);
                        cursorX = Math.Min(width - 1, prefix + _caret - start);
                    }

                    SmallRect target = info.Window;
                    Coord? cursor = _prompt != null && height >= 2
                        ? new Coord((short)(info.Window.Left + cursorX), (short)(info.Window.Bottom - 1)) : null;
                    if (!_terminal.Draw(cells, width, height, target, cursor))
                    {
                        Dispose();
                        return;
                    }
                    _window = info.Window;
                    _dirty = false;
                }
                catch (Exception)
                {
                    // An unavailable/closed console must never terminate a server worker.
                    Dispose();
                }
            }
        }

        static void Put(CharInfo[] cells, int width, int y, int x, string text, ConsoleColor color, ushort background)
        {
            for (int i = 0; i < text.Length && x + i < width; ++i)
                cells[y * width + x + i] = new CharInfo(text[i], (ushort)(background | (ushort)color));
        }

        void ReadInput()
        {
            var events = new InputRecord[64];
            try
            {
                while (_active && _terminal.ReadInput(events, out uint read))
                {
                    lock (_sync)
                    {
                        if (!_active)
                            return;
                        for (int i = 0; i < read; ++i)
                        {
                            var input = events[i];
                            if (input.Type == 2)
                                Mouse(input);
                            else if (input.Type == 1 && input.KeyDown != 0)
                                for (int repeat = 0; repeat < Math.Max(1, (int)input.Repeat); ++repeat)
                                    Key(input);
                            else if (input.Type == 4)
                                _dirty = true;
                        }
                    }
                }
            }
            catch (Exception) { /* Closing a terminal must not stop the server. */ }
            finally { Dispose(); }
        }

        void Mouse(InputRecord input)
        {
            if (input.MouseFlags == 4) // MOUSE_WHEELED; high word is signed wheel delta.
            {
                Scroll(-Math.Sign((short)(input.Buttons >> 16)) * 3);
                return;
            }
            bool down = (input.Buttons & 1) != 0;
            if (down && !_leftDown && input.MouseFlags is 0 or 2)
            {
                int x = input.Position.X - _window.Left;
                int y = input.Position.Y - _window.Top;
                if (x >= 0 && x < ConsoleLogLine.TimestampLength && y >= 0 && y < _visible.Length)
                {
                    var row = _visible[y];
                    if (row.HasTimestamp && row.Line.CanExpand)
                    {
                        row.Line.Expanded = !row.Line.Expanded;
                        _follow = false;
                        _version = -1;
                        _dirty = true;
                    }
                }
            }
            _leftDown = down;
        }

        void Scroll(int amount)
        {
            int bottom = Math.Max(0, _rows.Count - _logHeight);
            _top = Math.Clamp(_top + amount, 0, bottom);
            _follow = _top == bottom;
            _dirty = true;
        }

        void Key(InputRecord input)
        {
            bool control = (input.ControlKeys & (0x0004 | 0x0008)) != 0;
            switch (input.VirtualKey)
            {
                case 0x21: Scroll(-Math.Max(1, _logHeight - 1)); return; // PageUp
                case 0x22: Scroll(Math.Max(1, _logHeight - 1)); return; // PageDown
                case 0x23 when control || _prompt == null: _follow = true; _dirty = true; return;
                case 0x24 when control || _prompt == null: _top = 0; _follow = false; _dirty = true; return;
            }
            if (_prompt == null)
            {
                if (input.VirtualKey == 0x26) Scroll(-1);
                if (input.VirtualKey == 0x28) Scroll(1);
                return;
            }

            switch (input.VirtualKey)
            {
                case 0x0D:
                    string command = _input.ToString();
                    _buffer.Write((_prompt + command + "\n").AsSpan(), ConsoleColor.Green);
                    if (command.Length != 0 && (_history.Count == 0 || _history[^1] != command))
                    {
                        _history.Add(command);
                        if (_history.Count > 100) _history.RemoveAt(0);
                    }
                    _historyIndex = _history.Count;
                    _draft = "";
                    _input.Clear();
                    _caret = 0;
                    _follow = true;
                    _commands.Add(command);
                    break;
                case 0x08: if (_caret > 0) _input.Remove(--_caret, 1); break;
                case 0x2E: if (_caret < _input.Length) _input.Remove(_caret, 1); break;
                case 0x25: _caret = Math.Max(0, _caret - 1); break;
                case 0x27: _caret = Math.Min(_input.Length, _caret + 1); break;
                case 0x24: _caret = 0; break;
                case 0x23: _caret = _input.Length; break;
                case 0x26: History(-1); break;
                case 0x28: History(1); break;
                case 0x1B: _input.Clear(); _caret = 0; break;
                default:
                    if (!char.IsControl(input.Character))
                        _input.Insert(_caret++, input.Character);
                    break;
            }
            _dirty = true;
        }

        void History(int direction)
        {
            if (_historyIndex == _history.Count)
                _draft = _input.ToString();
            _historyIndex = Math.Clamp(_historyIndex + direction, 0, _history.Count);
            _input.Clear().Append(_historyIndex == _history.Count ? _draft : _history[_historyIndex]);
            _caret = _input.Length;
        }

        public void Dispose()
        {
            lock (_sync)
            {
                if (!_active)
                    return;
                _active = false;
                _timer.Dispose();
                _terminal.Dispose();
                _commands.CompleteAdding();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct Coord(short x, short y)
        {
            public readonly short X = x;
            public readonly short Y = y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SmallRect { public short Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ScreenInfo
        {
            public Coord Size, Cursor;
            public ushort Attributes;
            public SmallRect Window;
            public Coord MaximumSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct CursorInfo
        {
            public uint Size;
            [MarshalAs(UnmanagedType.Bool)] public bool Visible;
        }

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode, Size = 4)]
        internal struct CharInfo
        {
            [FieldOffset(0)] public char Character;
            [FieldOffset(2)] public ushort Attributes;
            public CharInfo(char character, ushort attributes) { Character = character; Attributes = attributes; }
        }

        [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode, Size = 20)]
        internal struct InputRecord
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(4)] public int KeyDown;
            [FieldOffset(8)] public ushort Repeat;
            [FieldOffset(10)] public ushort VirtualKey;
            [FieldOffset(14)] public char Character;
            [FieldOffset(16)] public uint ControlKeys;
            [FieldOffset(4)] public Coord Position;
            [FieldOffset(8)] public uint Buttons;
            [FieldOffset(16)] public uint MouseFlags;
        }

        internal interface ITerminal : IDisposable
        {
            bool GetScreen(out ScreenInfo info);
            bool Draw(CharInfo[] cells, int width, int height, SmallRect window, Coord? cursor);
            bool ReadInput(InputRecord[] events, out uint read);
        }
    }
}
