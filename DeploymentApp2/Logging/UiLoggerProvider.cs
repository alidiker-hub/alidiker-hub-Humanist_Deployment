using System;
using Microsoft.Extensions.Logging;

namespace DeploymentApp2.Logging
{
    public sealed class UiLoggerProvider : ILoggerProvider
    {
        private readonly UiLogSink _sink;
        private readonly LogLevel _minLevel;

        public UiLoggerProvider(UiLogSink sink, LogLevel minLevel = LogLevel.Information)
        {
            _sink = sink;
            _minLevel = minLevel;
        }

        public ILogger CreateLogger(string categoryName)
            => new UiLogger(_sink, categoryName, _minLevel);

        public void Dispose() { }
    }

    internal sealed class UiLogger : ILogger
    {
        private readonly UiLogSink _sink;
        private readonly string _category;
        private readonly LogLevel _min;

        public UiLogger(UiLogSink sink, string category, LogLevel min)
        {
            _sink = sink;
            _category = category;
            _min = min;
        }

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= _min;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                                Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            // Fazla gürültülü log kaynaklarını kırpmak istersen:
            if (_category.StartsWith("Microsoft.AspNetCore.Components.Server.Circuits.RemoteRenderer"))
                return; // render batch spam’ini kes

            var msg = formatter(state, exception);
            var item = new UiLogItem(DateTime.Now, logLevel, _category, msg, exception);
            _sink.Writer.TryWrite(item);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
