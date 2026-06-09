using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Data.Logging;

/// <summary>
/// <see cref="ILoggerProvider"/> that produces <see cref="DatabaseLogger"/>
/// instances feeding the shared <see cref="DatabaseLogChannel"/>. Registered as
/// a logging provider in both hosts so any <c>ILogger</c> call at or above the
/// configured minimum level is persisted by the background sink.
/// </summary>
/// <remarks>
/// The <c>[ProviderAlias("Database")]</c> attribute lets the standard
/// <c>Logging:Database:LogLevel</c> configuration section target this provider
/// independently of others, on top of the sink's own
/// <see cref="LoggingSinkOptions.MinimumLevel"/> gate.
/// </remarks>
[ProviderAlias("Database")]
internal sealed class DatabaseLoggerProvider : ILoggerProvider
{
    private readonly DatabaseLogChannel _channel;
    private readonly LoggingSinkOptions _options;
    private readonly string _host;
    private readonly ConcurrentDictionary<string, DatabaseLogger> _loggers = new(StringComparer.Ordinal);

    public DatabaseLoggerProvider(DatabaseLogChannel channel, IOptions<LoggingSinkOptions> options)
    {
        _channel = channel;
        _options = options.Value;
        _host = Environment.MachineName;
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new DatabaseLogger(name, _channel, _options, _host));

    public void Dispose() => _loggers.Clear();
}
