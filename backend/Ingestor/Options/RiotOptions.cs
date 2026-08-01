namespace Ingestor.Options;

public class RiotOptions
{
    public const string SectionName = "Riot";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Number of retries the resilience handler performs on a transient failure.
    /// Bounded to 10 by startup validation: unlike CommunityDragon's handler,
    /// this one *raises* <see cref="TotalRequestTimeoutSeconds"/> to fit the
    /// attempts rather than shrinking the attempt timeout, so an absurd retry
    /// count would inflate <see cref="EffectiveTotalRequestTimeout"/> — and
    /// therefore the ingestor's <c>HttpClient.Timeout</c> for the Riot clients —
    /// to an unreasonable length rather than failing fast (#855).
    /// </summary>
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
    /// <c>AttemptTimeoutSeconds * (MaxRetryAttempts + 1)</c> when configured lower
    /// — see <see cref="EffectiveTotalRequestTimeout"/> for the resolved value.
    /// </summary>
    public int TotalRequestTimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// The total-request-timeout the resilience handler actually applies, after
    /// its own invariant raises <see cref="TotalRequestTimeoutSeconds"/> to cover
    /// every attempt at <see cref="AttemptTimeoutSeconds"/>.
    /// </summary>
    /// <remarks>
    /// Both <c>Riot.RiotResilienceExtensions.AddRiotResilienceHandler</c> and
    /// <c>Ingestor/Program.cs</c>'s <c>ConfigureRiotClient</c> (which sizes the
    /// typed clients' <c>HttpClient.Timeout</c> around it) call this single
    /// calculation, so the outer client timeout can never fall behind the
    /// pipeline it wraps — the two used to be computed independently, and the
    /// client timeout was left at the 100s default entirely, silently
    /// truncating the pipeline whenever a Riot rate-limit backoff pushed the
    /// effective total past it (#855).
    /// </remarks>
    public TimeSpan EffectiveTotalRequestTimeout()
    {
        var configured = TimeSpan.FromSeconds(TotalRequestTimeoutSeconds);
        var minimumForRetries = TimeSpan.FromSeconds(AttemptTimeoutSeconds) * (MaxRetryAttempts + 1);
        return minimumForRetries > configured ? minimumForRetries : configured;
    }
}
