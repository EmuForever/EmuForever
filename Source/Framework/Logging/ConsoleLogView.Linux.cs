// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Framework.Logging
{
    sealed partial class ConsoleLogView
    {
        sealed class LinuxTerminal : ITerminal
        {
            readonly Termios _original;
            readonly StreamWriter _output;
            readonly AnsiInput _input = new();
            readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
            readonly byte[] _bytes = new byte[512];
            readonly char[] _characters = new char[Encoding.UTF8.GetMaxCharCount(512)];
            volatile bool _active = true;

            LinuxTerminal(Termios original)
            {
                _original = original;
                _output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            }

            public static LinuxTerminal TryCreate()
            {
                // Linux termios layout below is the glibc ABI used by x64/arm64 Ubuntu.
                if (RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)
                    || Environment.GetEnvironmentVariable("TERM") is null or "" or "dumb" or "linux" or "vt100"
                    || LibC.isatty(0) != 1 || LibC.isatty(1) != 1
                    || LibC.tcgetattr(0, out var original) != 0
                    || LibC.ioctl(1, 0x5413, out var size) != 0 || size.Columns < 20 || size.Rows < 3)
                    return null;

                var terminal = new LinuxTerminal(original);
                var raw = original;
                raw.Controls = (byte[])original.Controls.Clone();
                raw.InputFlags &= ~(0x0100u | 0x0400u); // ICRNL, IXON
                raw.LocalFlags &= ~(0x0002u | 0x0008u | 0x0040u | 0x8000u); // ICANON, ECHO, ECHONL, IEXTEN; retain ISIG/Ctrl+C.
                raw.Controls[6] = 0; // VMIN
                raw.Controls[5] = 1; // VTIME: 100ms, so shutdown/standalone Escape do not block forever.
                if (LibC.tcsetattr(0, 0, ref raw) != 0)
                    return null;
                try
                {
                    // Alternate screen preserves the shell; SGR mouse reports include press/release coordinates.
                    terminal._output.Write("\x1b[?1049h\x1b[?1000h\x1b[?1006h\x1b[?2004h\x1b[?25l\x1b[?7l\x1b[2J\x1b[H");
                    terminal._output.Flush();
                    return terminal;
                }
                catch
                {
                    terminal.Dispose();
                    return null;
                }
            }

            public bool GetScreen(out ScreenInfo info)
            {
                info = default;
                if (LibC.ioctl(1, 0x5413, out var size) != 0 || size.Columns == 0 || size.Rows == 0)
                    return false;
                info.Window = new SmallRect { Right = (short)(size.Columns - 1), Bottom = (short)(size.Rows - 1) };
                info.Attributes = 7;
                return true;
            }

            public bool Draw(CharInfo[] cells, int width, int height, SmallRect window, Coord? cursor)
            {
                var frame = new StringBuilder(cells.Length + height * 12);
                frame.Append("\x1b[?25l");
                int previous = -1;
                for (int y = 0; y < height; ++y)
                {
                    frame.Append("\x1b[").Append(y + 1).Append(";1H");
                    for (int x = 0; x < width; ++x)
                    {
                        var cell = cells[y * width + x];
                        int color = cell.Attributes & 15;
                        if (color != previous)
                        {
                            frame.Append("\x1b[").Append(AnsiColor(color)).Append('m');
                            previous = color;
                        }
                        frame.Append(cell.Character);
                    }
                }
                frame.Append("\x1b[0m");
                if (cursor.HasValue)
                    frame.Append("\x1b[").Append(cursor.Value.Y + 1).Append(';').Append(cursor.Value.X + 1).Append("H\x1b[?25h");
                _output.Write(frame);
                _output.Flush();
                return true;
            }

            // ConsoleColor's low three bits are BGR, while ANSI uses RGB order.
            static int AnsiColor(int color) => (color >= 8 ? 90 : 30)
                + ((color & 1) << 2) + (color & 2) + ((color & 4) >> 2);

            public bool ReadInput(InputRecord[] events, out uint read)
            {
                read = 0;
                var descriptors = new[] { new PollFd { Descriptor = 0, Events = 1 } };
                while (_active)
                {
                    while (read < events.Length && _input.TryRead(out var input))
                        events[read++] = input;
                    if (read != 0)
                        return true;

                    int ready = LibC.poll(descriptors, 1, 100);
                    if (ready < 0)
                    {
                        if (Marshal.GetLastPInvokeError() == 4) // EINTR
                            continue;
                        return false;
                    }
                    if (ready == 0)
                    {
                        _input.ExpireEscape();
                        continue;
                    }
                    if ((descriptors[0].ReturnedEvents & (8 | 16 | 32)) != 0) // Error, hangup, invalid fd.
                        return false;

                    nint count = LibC.read(0, _bytes, (nuint)_bytes.Length);
                    if (count < 0)
                    {
                        int error = Marshal.GetLastPInvokeError();
                        if (error is 4 or 11) // EINTR/EAGAIN
                            continue;
                        return false;
                    }
                    if (count == 0)
                        return false;
                    int characters = _decoder.GetChars(_bytes.AsSpan(0, (int)count), _characters, false);
                    _input.Feed(_characters.AsSpan(0, characters));
                }
                return false;
            }

            public void Dispose()
            {
                if (!_active)
                    return;
                _active = false;
                var original = _original;
                LibC.tcsetattr(0, 0, ref original);
                try
                {
                    _output.Write("\x1b[0m\x1b[?1000l\x1b[?1006l\x1b[?2004l\x1b[?7h\x1b[?25h\x1b[?1049l");
                    _output.Flush();
                }
                catch (IOException) { }
            }

            [StructLayout(LayoutKind.Sequential)]
            struct Termios
            {
                public uint InputFlags, OutputFlags, ControlFlags, LocalFlags;
                public byte Line;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Controls;
                public uint InputSpeed, OutputSpeed;
            }

            [StructLayout(LayoutKind.Sequential)]
            struct WindowSize { public ushort Rows, Columns, PixelWidth, PixelHeight; }

            [StructLayout(LayoutKind.Sequential)]
            struct PollFd { public int Descriptor; public short Events, ReturnedEvents; }

            static class LibC
            {
                [DllImport("libc", SetLastError = true)] public static extern int isatty(int fd);
                [DllImport("libc", SetLastError = true)] public static extern int tcgetattr(int fd, out Termios attributes);
                [DllImport("libc", SetLastError = true)] public static extern int tcsetattr(int fd, int action, ref Termios attributes);
                [DllImport("libc", SetLastError = true)] public static extern int ioctl(int fd, nuint request, out WindowSize size);
                [DllImport("libc", SetLastError = true)] public static extern nint read(int fd, [Out] byte[] buffer, nuint count);
                [DllImport("libc", SetLastError = true)] public static extern int poll([In, Out] PollFd[] descriptors, nuint count, int timeout);
            }
        }

        // Incremental parser: terminal/SSH reads can split an escape sequence at any byte boundary.
        internal sealed class AnsiInput
        {
            readonly Queue<InputRecord> _events = new();
            readonly StringBuilder _escape = new();
            bool _carriageReturn;

            public bool TryRead(out InputRecord input) => _events.TryDequeue(out input);

            public void Feed(ReadOnlySpan<char> text)
            {
                foreach (char character in text)
                {
                    if (_escape.Length == 0)
                    {
                        if (character == '\x1b') _escape.Append(character);
                        else Character(character);
                        continue;
                    }

                    _escape.Append(character);
                    if (_escape.Length == 2)
                    {
                        if (character is not ('[' or 'O'))
                        {
                            _escape.Clear();
                            Character(character); // Alt+character
                        }
                    }
                    else if (character is >= '@' and <= '~')
                    {
                        Sequence(_escape.ToString());
                        _escape.Clear();
                    }
                    else if (_escape.Length > 80)
                        _escape.Clear();
                }
            }

            public void ExpireEscape()
            {
                if (_escape.Length != 0)
                {
                    _escape.Clear();
                    Emit(0x1B);
                }
            }

            void Character(char character)
            {
                if (_carriageReturn && character == '\n')
                {
                    _carriageReturn = false;
                    return;
                }
                _carriageReturn = character == '\r';
                ushort key = character switch
                {
                    '\r' or '\n' => 0x0D,
                    '\b' or '\x7f' => 0x08,
                    _ => 0
                };
                Emit(key, character);
            }

            void Sequence(string sequence)
            {
                char final = sequence[^1];
                string parameters = sequence[2..^1];
                if (parameters.StartsWith('<') && final is 'M' or 'm')
                {
                    string[] parts = parameters[1..].Split(';');
                    if (parts.Length != 3 || !int.TryParse(parts[0], out int button)
                        || !short.TryParse(parts[1], out short x) || !short.TryParse(parts[2], out short y))
                        return;
                    bool wheel = (button & 64) != 0;
                    uint buttons = wheel ? unchecked((uint)((button & 1) == 0 ? 120 : -120) << 16)
                        : final == 'M' && (button & 3) == 0 ? 1u : 0u;
                    _events.Enqueue(new InputRecord { Type = 2, Position = new Coord((short)(x - 1), (short)(y - 1)),
                        Buttons = buttons, MouseFlags = wheel ? 4u : (button & 32) != 0 ? 1u : 0u });
                    return;
                }

                string[] values = parameters.Split(';');
                uint control = values.Length > 1 && int.TryParse(values[^1], out int modifier) && ((modifier - 1) & 4) != 0 ? 8u : 0u;
                ushort key = final switch
                {
                    'A' => 0x26, 'B' => 0x28, 'C' => 0x27, 'D' => 0x25,
                    'H' => 0x24, 'F' => 0x23,
                    '~' => values[0] switch
                    {
                        "1" or "7" => 0x24, "4" or "8" => 0x23, "3" => 0x2E,
                        "5" => 0x21, "6" => 0x22, _ => 0
                    },
                    _ => 0
                };
                if (key != 0)
                    Emit(key, control: control);
            }

            void Emit(ushort key, char character = '\0', uint control = 0) => _events.Enqueue(new InputRecord
            {
                Type = 1, KeyDown = 1, Repeat = 1, VirtualKey = key, Character = character, ControlKeys = control
            });
        }
    }
}
