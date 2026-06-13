using System.Threading.Channels;
using Data.Logging.Mongo;
using Microsoft.Extensions.Options;

namespace Data.Metrics.Mongo;

/// <summary>
/// Shared, bounded in-memory queue bridging the synchronous Riot HTTP metrics
/// handler (producer) and the asynchronous draining
/// <see cref="RiotApiMetricsSink"/> (consumer). Registered as a singleton so both
/// sides see the same channel.
/// </summary>
/// <remarks>
/// Mirrors <c>MongoLogChannel</c>: bounded with
/// <see cref="BoundedChannelFullMode.DropOldest"/> so a burst of Riot calls can
/// never block the HTTP path or grow memory without bound — the oldest queued
/// metric is discarded instead. Metrics are non-critical telemetry, so dropping
/// under sustained pressure is the deliberate trade-off. Capacity reuses the
/// shared <see cref="MongoLoggingOptions.Capacity"/> knob.
/// </remarks>
internal sealed class RiotApiMetricsChannel
{
    private readonly Channel<RiotApiCallRecord> _channel;

    public RiotApiMetricsChannel(IOptions<MongoLoggingOptions> options)
    {
        var capacity = Math.Max(1, options.Value.Capacity);
        _channel = Channel.CreateBounded<RiotApiCallRecord>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<RiotApiCallRecord> Reader => _channel.Reader;

    /// <summary>
    /// Non-blocking enqueue. Returns false only if the channel has been completed
    /// (shutdown); a full channel still "succeeds" because DropOldest evicts to
    /// make room.
    /// </summary>
    public bool TryWrite(RiotApiCallRecord record) => _channel.Writer.TryWrite(record);

    public void Complete() => _channel.Writer.TryComplete();
}
