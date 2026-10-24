// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;

public class Cypher
{
    public static void Assert(bool value, string message = "", [CallerMemberName]string memberName = "")
    {
        if (!value)
        {
            if (!message.IsEmpty())
                Log.outFatal(LogFilter.Server, message);

            throw new Exception(memberName);
        }
    }

    // Interpolated messages are formatted only when the assertion fails. Several asserts run on every
    // creature update; formatting their GUIDs each time cost as much as the movement itself.
    public static void Assert(bool value, [InterpolatedStringHandlerArgument("value")] ref AssertMessage message, [CallerMemberName] string memberName = "")
    {
        if (!value)
        {
            string text = message.ToStringAndClear();
            if (!text.IsEmpty())
                Log.outFatal(LogFilter.Server, text);

            throw new Exception(memberName);
        }
    }
}

[InterpolatedStringHandler]
public ref struct AssertMessage
{
    DefaultInterpolatedStringHandler _text;
    readonly bool _enabled;
    public AssertMessage(int literalLength, int formattedCount, bool value, out bool shouldAppend)
    {
        _enabled = !value;
        shouldAppend = _enabled;
        _text = _enabled ? new(literalLength, formattedCount) : default;
    }
    public void AppendLiteral(string value) => _text.AppendLiteral(value);
    public void AppendFormatted<T>(T value) => _text.AppendFormatted(value);
    public void AppendFormatted<T>(T value, string format) => _text.AppendFormatted(value, format);
    public void AppendFormatted<T>(T value, int alignment) => _text.AppendFormatted(value, alignment);
    public void AppendFormatted<T>(T value, int alignment, string format) => _text.AppendFormatted(value, alignment, format);
    public string ToStringAndClear() => _enabled ? _text.ToStringAndClear() : string.Empty;
}
