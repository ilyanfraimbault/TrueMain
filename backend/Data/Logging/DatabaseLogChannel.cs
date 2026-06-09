using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Data.Logging;

/// <summary>
/// Shared, bounded in-memory queue bridging the synchronous logging callers
/// (via <see cref="DatabaseLogger"/>) and the asynchronous draining
/// <see cref="DatabaseLogSink"/>. Registered as a singleton so producer and
/// consumer see the same channel.
/// </summary>
/// <remarks>
/// The channel is bounded with <see cref="BoundedChannelFullMode.DropOldest"/>:
/// a logging burst can never block the caller or grow memory without limit; the
/// oldest queued record is discarded instead. Logging must stay non-blocking,
/// so dropping under sustained pressure is the deliberate trade-off.
/// </remarks>
internal sealed class DatabaseLogChannel
{
    private readonly Channel<LogRecord> _channel;

    public DatabaseLogChannel(IOptions<LoggingSinkOptions> options)
    {
        var capacity = Math.Max(1, options.Value.Capacity);
        _channel = Channel.CreateBounded<LogRecord>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<LogRecord> Reader => _channel.Reader;

    /// <summary>
    /// Non-blocking enqueue. Returns false only if the channel has been
    /// completed (shutdown); a full channel still "succeeds" because the
    /// DropOldest policy evicts to make room.
    /// </summary>
    public bool TryWrite(LogRecord record) => _channel.Writer.TryWrite(record);

    public void Complete() => _channel.Writer.TryComplete();
}
