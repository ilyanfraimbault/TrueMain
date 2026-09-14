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
/// <para>
/// Dropping is deliberate, dropping <em>silently</em> is not (#1555): every evicted
/// record is counted, and the sink turns the count into a
/// <see cref="OpsEvents.LogRecordsDropped"/> row, so a burst that lost its first
/// errors says so instead of looking quieter than it was.
/// </para>
/// </remarks>
internal sealed class MongoLogChannel
{
    private readonly Channel<MongoLogRecord> _channel;
    private long _dropped;

    public MongoLogChannel(IOptions<MongoLoggingOptions> options)
    {
        Capacity = Math.Max(1, options.Value.Capacity);
        _channel = Channel.CreateBounded<MongoLogRecord>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            _ => Interlocked.Increment(ref _dropped));
    }

    public ChannelReader<MongoLogRecord> Reader => _channel.Reader;

    /// <summary>How many records the channel holds before it starts evicting the oldest.</summary>
    public int Capacity { get; }

    /// <summary>Records evicted since the previous call, resetting the count.</summary>
    public long TakeDroppedCount() => Interlocked.Exchange(ref _dropped, 0);

    /// <summary>
    /// Non-blocking enqueue. Returns false only if the channel has been completed
    /// (shutdown); a full channel still "succeeds" because the DropOldest policy
    /// evicts to make room.
    /// </summary>
    public bool TryWrite(MongoLogRecord record) => _channel.Writer.TryWrite(record);

    public void Complete() => _channel.Writer.TryComplete();
}
