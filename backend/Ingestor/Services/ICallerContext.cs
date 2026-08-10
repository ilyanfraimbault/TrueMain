namespace Ingestor.Services;

/// <summary>
/// Carries the name of the <c>IIngestorProcess</c> currently executing on
/// the calling async flow (e.g. <c>Discovery</c>, <c>MatchIngestion</c>). The
/// Worker opens a call scope around each process's <c>RunCoreAsync</c>;
/// <c>RiotApiMetricsHandler</c> reads <see cref="CurrentCaller"/> when it
/// records a Riot API call, so consumption can be attributed per caller (#1035).
/// Backed by an <c>AsyncLocal</c>, so the value flows down the await chain
/// without being shared across concurrent processes — a call made outside any
/// tracked process simply reads <see langword="null"/>.
/// </summary>
public interface ICallerContext
{
    /// <summary>The process name the calling flow is running under, or null when outside one.</summary>
    string? CurrentCaller { get; }

    /// <summary>
    /// Opens a call scope on the calling async flow and returns it. The caller
    /// name is in effect until the returned scope is disposed, which restores the
    /// prior value (so nested or sequential processes don't leak into one another).
    /// </summary>
    IDisposable BeginCall(string processName);
}
