// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;

namespace Framework.Logging
{
    sealed partial class ConsoleLogView
    {
        sealed class WindowsTerminal(IntPtr input, IntPtr output, uint mode, CursorInfo cursor) : ITerminal
        {
            public static WindowsTerminal TryCreate()
            {
                IntPtr input = Win32.GetStdHandle(-10);
                IntPtr output = Win32.GetStdHandle(-11);
                if (!Win32.GetConsoleMode(input, out uint mode) || !Win32.GetConsoleScreenBufferInfo(output, out var info)
                    || info.Window.Right - info.Window.Left < 19 || info.Window.Bottom - info.Window.Top < 2
                    || !Win32.GetConsoleCursorInfo(output, out var cursor))
                    return null;

                // Retain Ctrl+C; mouse/resize input replaces QuickEdit and ReadConsole's line/echo modes.
                const uint enable = 0x0001 | 0x0008 | 0x0010 | 0x0080;
                const uint disable = 0x0002 | 0x0004 | 0x0040 | 0x0200;
                return Win32.SetConsoleMode(input, (mode | enable) & ~disable)
                    ? new WindowsTerminal(input, output, mode, cursor) : null;
            }

            public bool GetScreen(out ScreenInfo info) => Win32.GetConsoleScreenBufferInfo(output, out info);
            public bool ReadInput(InputRecord[] events, out uint read) => Win32.ReadConsoleInputW(input, events, (uint)events.Length, out read);

            public bool Draw(CharInfo[] cells, int width, int height, SmallRect window, Coord? position)
            {
                if (!Win32.WriteConsoleOutputW(output, cells, new Coord((short)width, (short)height), default, ref window))
                    return false;
                var cursorInfo = new CursorInfo { Size = cursor.Size, Visible = position.HasValue };
                Win32.SetConsoleCursorInfo(output, ref cursorInfo);
                if (position.HasValue)
                    Win32.SetConsoleCursorPosition(output, position.Value);
                return true;
            }

            public void Dispose()
            {
                Win32.SetConsoleMode(input, mode);
                var cursorInfo = cursor;
                Win32.SetConsoleCursorInfo(output, ref cursorInfo);
            }
        }

        static class Win32
        {
            [DllImport("kernel32.dll")] public static extern IntPtr GetStdHandle(int handle);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetConsoleMode(IntPtr handle, out uint mode);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetConsoleMode(IntPtr handle, uint mode);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetConsoleScreenBufferInfo(IntPtr handle, out ScreenInfo info);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetConsoleCursorInfo(IntPtr handle, out CursorInfo info);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetConsoleCursorInfo(IntPtr handle, ref CursorInfo info);
            [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetConsoleCursorPosition(IntPtr handle, Coord position);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool WriteConsoleOutputW(IntPtr handle, [In] CharInfo[] buffer, Coord size, Coord origin, ref SmallRect target);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool ReadConsoleInputW(IntPtr handle, [Out] InputRecord[] buffer, uint length, out uint read);
        }
    }
}
