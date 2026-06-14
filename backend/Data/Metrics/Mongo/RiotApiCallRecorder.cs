using Data.Logging.Mongo;
using Microsoft.Extensions.Options;

namespace Data.Metrics.Mongo;

/// <summary>
/// Default <see cref="IRiotApiCallRecorder"/>: a thin, non-blocking producer that
/// drops each record onto the shared <see cref="RiotApiMetricsChannel"/> for the
/// background <see cref="RiotApiMetricsSink"/> to persist. Singleton.
/// </summary>
/// <remarks>
/// The producing-host tag (<see cref="MongoLoggingOptions.ProcessName"/>) is
/// stamped by the sink when it builds the document, not carried per record, so
/// the hot-path record stays a pure per-call value.
/// </remarks>
internal sealed class RiotApiCallRecorder(
    RiotApiMetricsChannel channel,
    IOptions<MongoLoggingOptions> options) : IRiotApiCallRecorder
{
    private readonly bool _active = options.Value.IsActive;

    public void Record(RiotApiCallRecord record)
    {
        // No Mongo configured ⇒ nothing to persist to. Skip the enqueue so the
        // channel never fills with records that can't drain.
        if (!_active)
        {
            return;
        }

        channel.TryWrite(record);
    }
}
