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
/// The <c>[ProviderAlias("Mongo")]</c> attribute lets the standard
/// <c>Logging:Mongo:LogLevel</c> configuration section target this provider
/// independently of others, on top of the sink's own
/// <see cref="MongoLoggingOptions.MinimumLevel"/> gate.
/// </remarks>
[ProviderAlias("Mongo")]
internal sealed class MongoLoggerProvider : ILoggerProvider
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

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new MongoLogger(name, _channel, _options, _host));

    public void Dispose() => _loggers.Clear();
}
