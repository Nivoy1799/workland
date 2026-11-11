using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Worktown.Gateway.Tests;

/// <summary>Captures every log entry at every level: message, state values, scopes and exceptions.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<string> _entries = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<string> Entries => _entries.ToArray();

    public string AllText => string.Join("\n", _entries);

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _owner;
        private readonly string _category;

        public CapturingLogger(CapturingLoggerProvider owner, string category)
        {
            _owner = owner;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var sb = new StringBuilder();
            sb.Append(logLevel).Append(' ').Append(_category).Append(": ").Append(formatter(state, exception));
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                foreach (var kv in values) sb.Append(" | ").Append(kv.Key).Append('=').Append(kv.Value);
            }
            _owner._scopes.ForEachScope((scope, builder) => builder.Append(" scope=").Append(scope), sb);
            if (exception is not null) sb.Append(' ').Append(exception);
            _owner._entries.Enqueue(sb.ToString());
        }
    }
}
