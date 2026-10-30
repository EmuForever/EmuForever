// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Framework.Logging
{
    // Display history only. File/database appenders continue receiving the complete original messages.
    sealed class ConsoleLogBuffer
    {
        public const int PreviewLength = 100;
        const int HistoryLines = 5000;
        const int HistoryCharacters = 2_000_000;
        readonly List<ConsoleLogLine> _lines = new();
        ConsoleLogLine _current;
        bool _carriageReturn;
        int _characters;

        public long Version { get; private set; }

        public void Write(ReadOnlySpan<char> text, ConsoleColor color)
        {
            if (text.IsEmpty)
                return;

            foreach (char character in text)
            {
                if (_carriageReturn && character == '\n')
                {
                    _carriageReturn = false;
                    continue;
                }

                if (_current == null)
                {
                    _current = new ConsoleLogLine(DateTime.Now);
                    _lines.Add(_current);
                }

                _carriageReturn = character == '\r';
                if (character is '\r' or '\n')
                    _current = null;
                else
                {
                    _current.Append(character, color);
                    ++_characters;
                }
            }

            int remove = 0;
            while (_lines.Count - remove > 1
                && (_lines.Count - remove > HistoryLines || _characters > HistoryCharacters))
                _characters -= _lines[remove++].Length;
            if (remove != 0)
                _lines.RemoveRange(0, remove);
            ++Version;
        }

        public List<ConsoleLogRow> Layout(int width)
        {
            var rows = new List<ConsoleLogRow>(_lines.Count);
            int bodyWidth = Math.Max(1, width - ConsoleLogLine.PrefixLength);
            foreach (var line in _lines)
            {
                int length = line.VisibleLength;
                int offset = 0;
                do
                {
                    int count = Math.Min(bodyWidth, length - offset);
                    // Keep UTF-16 surrogate pairs together when a line wraps.
                    if (count > 1 && offset + count < length && char.IsHighSurrogate(line.CharacterAt(offset + count - 1))
                        && char.IsLowSurrogate(line.CharacterAt(offset + count)))
                        --count;
                    rows.Add(new ConsoleLogRow(line, offset, count));
                    offset += count;
                } while (offset < length);
            }
            return rows;
        }
    }

    sealed class ConsoleLogLine(DateTime time)
    {
        public const int TimestampLength = 10;
        public const int PrefixLength = TimestampLength + 2;
        readonly StringBuilder _text = new();
        readonly List<ConsoleColor> _colors = new();

        public string Timestamp { get; } = "[" + time.ToString("hh:mm tt", CultureInfo.InvariantCulture) + "]";
        public bool Expanded { get; set; }
        public int Length => _text.Length;
        public bool CanExpand => Length > ConsoleLogBuffer.PreviewLength;
        public int PreviewLength => CanExpand && char.IsHighSurrogate(_text[ConsoleLogBuffer.PreviewLength - 1])
            && char.IsLowSurrogate(_text[ConsoleLogBuffer.PreviewLength])
                ? ConsoleLogBuffer.PreviewLength - 1 : Math.Min(Length, ConsoleLogBuffer.PreviewLength);
        public int VisibleLength => !Expanded && CanExpand ? PreviewLength + 3 : Length;

        public void Append(char value, ConsoleColor color)
        {
            _text.Append(value);
            _colors.Add(color);
        }

        public char CharacterAt(int offset)
        {
            if (!Expanded && CanExpand && offset >= PreviewLength)
                return '.';
            char value = _text[offset];
            // Control characters must not move the cursor outside their assigned display cells.
            return char.IsControl(value) ? ' ' : value;
        }

        public ConsoleColor ColorAt(int offset) => !Expanded && CanExpand && offset >= PreviewLength
            ? ConsoleColor.DarkGray : _colors[offset];
    }

    readonly record struct ConsoleLogRow(ConsoleLogLine Line, int Offset, int Count)
    {
        public bool HasTimestamp => Offset == 0;
    }
}
