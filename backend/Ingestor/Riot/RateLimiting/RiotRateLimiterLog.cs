namespace Ingestor.Riot.RateLimiting;

/// <summary>
/// The rate limiter's log lines, source-generated (#1636): they sit on the path of every Riot
/// call, where a template parsed and arguments boxed per call are pure overhead.
/// </summary>
internal static partial class RiotRateLimiterLog
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Rate-limit wait for {RoutingValue}/{Endpoint} would exceed {MaxWaitSeconds}s; sending anyway.")]
    public static partial void PermitWaitCeilingExceeded(
        this ILogger logger, string routingValue, string endpoint, int maxWaitSeconds);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Riot rate limit hit on {RoutingValue}/{Endpoint} (type {LimitType}); backing off {RetryAfterSeconds}s.")]
    public static partial void RateLimitHit(
        this ILogger logger, string routingValue, string endpoint, string limitType, double retryAfterSeconds);
}
