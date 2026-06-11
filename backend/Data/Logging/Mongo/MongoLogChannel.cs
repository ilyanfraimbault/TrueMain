using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Data.Logging.Mongo;

/// <summary>
/// Shared, bounded in-memory queue bridging the synchronous logging callers (via
/// <see cref="MongoLogger"/>) and the asynchronous draining
/// <see cref="MongoLogSink"/>. Registered as a singleton so producer and consumer
/// see the same channel.
/// </summary>
/// <remarks>
/// The channel is bounded with <see cref="BoundedChannelFullMode.DropOldest"/>: a
/// logging burst can never block the caller or grow memory without limit; the
/// oldest queued record is discarded instead. Diagnostic logging must stay
/// non-blocking, so dropping under sustained pressure is the deliberate
/// trade-off. The lossless audit path (<see cref="MongoAuditLog"/>) deliberately
/// does <em>not</em> go through this channel.
/// </remarks>
internal sealed class MongoLogChannel
{
    private readonly Channel<MongoLogRecord> _channel;

    public MongoLogChannel(IOptions<MongoLoggingOptions> options)
    {
        var capacity = Math.Max(1, options.Value.Capacity);
        _channel = Channel.CreateBounded<MongoLogRecord>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<MongoLogRecord> Reader => _channel.Reader;

    /// <summary>
    /// Non-blocking enqueue. Returns false only if the channel has been completed
    /// (shutdown); a full channel still "succeeds" because the DropOldest policy
    /// evicts to make room.
    /// </summary>
    public bool TryWrite(MongoLogRecord record) => _channel.Writer.TryWrite(record);

    public void Complete() => _channel.Writer.TryComplete();
}
