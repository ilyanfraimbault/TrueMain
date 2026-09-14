using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Data.Logging.Mongo;

/// <summary>
/// <see cref="ILoggerProvider"/> that produces <see cref="MongoLogger"/>
/// instances feeding the shared <see cref="MongoLogChannel"/>. Registered as a
/// logging provider in both hosts so any <c>ILogger</c> call at or above the
/// configured minimum level is persisted by the background sink.
/// </summary>
/// <remarks>
/// <para>
/// The <c>[ProviderAlias("Mongo")]</c> attribute lets the standard
/// <c>Logging:Mongo:LogLevel</c> configuration section target this provider
/// independently of others, on top of the sink's own
/// <see cref="MongoLoggingOptions.MinimumLevel"/> gate. Both hosts use it to
/// silence Polly resilience telemetry below Error (<c>"Polly": "Error"</c>):
/// retry chatter ("Execution attempt" / "OnRetry" Warnings on every Riot 429) is
/// dropped before this provider is even called, while Error-severity events such
/// as the circuit breaker opening are still persisted (#444). Console providers
/// are unaffected, so the noise can stay visible on stdout.
/// </para>
/// <para>
/// It implements <see cref="ISupportExternalScope"/> so a record written during an
/// HTTP request can carry that request's path and trace identifier (#1555): the
/// logger factory hands every scope-aware provider the shared scope stack, and
/// <see cref="MongoLogger"/> reads it when it snapshots a record.
/// </para>
/// </remarks>
[ProviderAlias("Mongo")]
internal sealed class MongoLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly MongoLogChannel _channel;
    private readonly MongoLoggingOptions _options;
    private readonly string _host;
    private readonly ConcurrentDictionary<string, MongoLogger> _loggers = new(StringComparer.Ordinal);

    public MongoLoggerProvider(MongoLogChannel channel, IOptions<MongoLoggingOptions> options)
    {
        _channel = channel;
        _options = options.Value;
        _host = Environment.MachineName;
    }

    /// <summary>The scope stack set by the logger factory; null until it has been set.</summary>
    internal IExternalScopeProvider? ScopeProvider { get; private set; }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new MongoLogger(name, _channel, _options, _host, this));

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => ScopeProvider = scopeProvider;

    public void Dispose() => _loggers.Clear();
}
