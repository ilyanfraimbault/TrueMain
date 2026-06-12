namespace Ingestor.Options;

public class RiotOptions
{
    public const string SectionName = "Riot";

    public string ApiKey { get; set; } = string.Empty;

    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Timeout, in seconds, applied to each individual HTTP attempt against the
    /// Riot API. Riot answers quickly even when throttling (a 429 is immediate),
    /// so this matches the standard resilience handler's 10s default.
    /// </summary>
    public int AttemptTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Timeout, in seconds, applied to a request's total execution: every attempt
    /// plus every wait between retries. The retry strategy honours Riot's
    /// <c>Retry-After</c> headers, and app-rate-limit windows can demand waits in
    /// excess of 100 seconds, so this must comfortably cover at least one such
    /// wait followed by a successful attempt. The handler raises it to
    /// <c>AttemptTimeoutSeconds * (MaxRetryAttempts + 1)</c> when configured lower.
    /// </summary>
    public int TotalRequestTimeoutSeconds { get; set; } = 180;
}
