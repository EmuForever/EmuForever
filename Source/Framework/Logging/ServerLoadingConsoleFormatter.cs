// Copyright (c) CypherCore <http://github.com/CypherCore> All rights reserved.
// Licensed under the GNU GENERAL PUBLIC LICENSE. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

// Owned by the console writer thread. Other appenders keep the original, separate log records.
sealed class ServerLoadingConsoleFormatter
{
    public ServerLoadingConsoleFormatter(Action<ConsoleColor, string, bool> write)
    {
        _write = write;
    }

    public void Write(LogLevel level, LogFilter type, string prefix, string text, int threadId, ConsoleColor color)
    {
        bool failure = level is LogLevel.Error or LogLevel.Fatal
            || (type == LogFilter.ServerLoading && level == LogLevel.Info && IsFailure(text));

        if (type == LogFilter.ServerLoading && level == LogLevel.Info && !failure && IsStart(text))
        {
            CloseLine();
            string start = Format(prefix, text).TrimEnd();
            _pending[threadId] = start;
            _write(ConsoleColor.White, start, false);
            _openThread = threadId;
            return;
        }

        // Several loaders report their result/error under Server or Sql rather than ServerLoading.
        // Match only the announcing thread, so unrelated worker messages cannot complete its step.
        if (_pending.TryGetValue(threadId, out string loading)
            && (failure || (level == LogLevel.Info && (type == LogFilter.ServerLoading || IsCompletion(text)))))
        {
            if (_openThread != threadId)
            {
                CloseLine();
                _write(ConsoleColor.White, loading, false);
            }

            _write(failure ? ConsoleColor.Red : ConsoleColor.Green, " " + (failure ? FailureText(text) : text), true);
            _openThread = null;
            _pending.Remove(threadId);
            return;
        }

        CloseLine();
        if (type == LogFilter.ServerLoading)
        {
            if (failure)
            {
                color = ConsoleColor.Red;
                text = FailureText(text);
            }
            else if (level == LogLevel.Info)
                color = ConsoleColor.Green;
        }
        else if (level == LogLevel.Info)
            _pending.Remove(threadId);

        _write(color, Format(prefix, text), true);
    }

    public void Flush()
    {
        CloseLine();
        _pending.Clear();
    }

    void CloseLine()
    {
        if (_openThread.HasValue)
        {
            _write(ConsoleColor.White, string.Empty, true);
            _openThread = null;
        }
    }

    static string Format(string prefix, string text) => $" {prefix} {text}";

    static bool IsStart(string text)
    {
        string value = text.TrimStart();
        return StartsWithWord(value, "Loading") || StartsWithWord(value, "Initialize") || StartsWithWord(value, "Initializing")
            || StartsWithWord(value, "Starting") || StartsWithWord(value, "Generating") || StartsWithWord(value, "Validating")
            || StartsWithWord(value, "Checking") || StartsWithWord(value, "Cleaning") || StartsWithWord(value, "Deleting")
            || StartsWithWord(value, "Parsing") || StartsWithWord(value, "Calculate") || StartsWithWord(value, "Filling")
            || StartsWithWord(value, "Returning");
    }

    static bool IsCompletion(string text)
    {
        string value = text.TrimStart().TrimStart('>', ' ');
        return StartsWithWord(value, "Loaded") || StartsWithWord(value, "Initialized") || StartsWithWord(value, "Generated")
            || StartsWithWord(value, "Validated") || StartsWithWord(value, "Checked") || StartsWithWord(value, "Cleaned")
            || StartsWithWord(value, "Parsed") || StartsWithWord(value, "Spawned") || StartsWithWord(value, "Done")
            || StartsWithWord(value, "Completed") || value.Contains(" loaded in ", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" system initialized", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsFailure(string text) => StartsWithWord(text.TrimStart(), "Failed") || StartsWithWord(text.TrimStart(), "Unable");

    static string FailureText(string text) => StartsWithWord(text.TrimStart(), "Failed") ? text : "Failed: " + text;

    static bool StartsWithWord(string text, string word) => text.StartsWith(word, StringComparison.OrdinalIgnoreCase)
        && (text.Length == word.Length || char.IsWhiteSpace(text[word.Length]) || text[word.Length] is '.' or ':' or '…');

    readonly Action<ConsoleColor, string, bool> _write;
    readonly Dictionary<int, string> _pending = new();
    int? _openThread;
}
