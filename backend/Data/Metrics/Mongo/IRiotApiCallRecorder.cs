namespace Data.Metrics.Mongo;

/// <summary>
/// Write side of the Riot API usage metrics store. The Ingestor's HTTP metrics
/// handler calls <see cref="Record"/> once per Riot request attempt; the call is
/// non-blocking (it only enqueues onto an in-memory channel) and never throws, so
/// instrumentation can never slow down or break a Riot call. Lives in the Data
/// layer so the Ingestor depends only on this interface and the
/// <see cref="RiotApiCallRecord"/> contract.
/// </summary>
public interface IRiotApiCallRecorder
{
    /// <summary>
    /// Enqueues a single Riot API call for background persistence. Fire-and-forget:
    /// dropped silently when metrics are disabled (no Mongo configured) or the
    /// channel is saturated.
    /// </summary>
    void Record(RiotApiCallRecord record);
}
