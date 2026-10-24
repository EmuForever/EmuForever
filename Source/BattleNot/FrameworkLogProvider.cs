using MsLogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace BattleNot;

/// <summary>Routes ASP.NET Core / Kestrel logging into the Framework Log (LogFilter.Http).</summary>
sealed class FrameworkLogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FrameworkLogger(categoryName);

    public void Dispose() { }

    sealed class FrameworkLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(MsLogLevel logLevel) => logLevel != MsLogLevel.None;

        public void Log<TState>(MsLogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string text = $"[{category}] {formatter(state, exception)}";
            if (exception != null)
                text += Environment.NewLine + exception;

            switch (logLevel)
            {
                case MsLogLevel.Trace:
                case MsLogLevel.Debug:
                    global::Log.outDebug(LogFilter.Http, text);
                    break;
                case MsLogLevel.Information:
                    global::Log.outInfo(LogFilter.Http, text);
                    break;
                case MsLogLevel.Warning:
                    global::Log.outWarn(LogFilter.Http, text);
                    break;
                case MsLogLevel.Error:
                    global::Log.outError(LogFilter.Http, text);
                    break;
                case MsLogLevel.Critical:
                    global::Log.outFatal(LogFilter.Http, text);
                    break;
            }
        }
    }
}
