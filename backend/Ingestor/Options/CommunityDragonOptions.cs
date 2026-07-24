namespace Ingestor.Options;

/// <summary>
/// Settings for the CommunityDragon item-metadata HTTP client. CommunityDragon is a
/// community-run mirror of Riot's game data: it is flakier than the Riot API itself and
/// regularly lags behind on patch day, so its fetches run behind a resilience pipeline.
/// </summary>
public class CommunityDragonOptions
{
    public const string SectionName = "CommunityDragon";

    /// <summary>
    /// Number of retries the resilience handler performs on a transient failure. The
    /// metadata payload is fetched at most once per patch and then cached in-process, so
    /// a handful of retries costs nothing while covering a mirror hiccup.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Timeout, in seconds, applied to each individual HTTP attempt. CommunityDragon
    /// serves a multi-megabyte static JSON file, so this is more generous than the
    /// standard handler's 10s default.
    /// </summary>
    public int AttemptTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// Timeout, in seconds, applied to a request's total execution: every attempt plus
    /// every backoff wait between them. Unlike Riot, CommunityDragon never asks for a
    /// multi-minute <c>Retry-After</c> wait, so this stays a hard ceiling: the handler
    /// clamps the per-attempt timeout down to fit instead of stretching the total.
    /// </summary>
    public int TotalRequestTimeoutSeconds { get; set; } = 75;
}
