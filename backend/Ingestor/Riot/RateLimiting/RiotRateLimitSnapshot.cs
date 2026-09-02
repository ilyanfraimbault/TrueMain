namespace Ingestor.Riot.RateLimiting;

/// <summary>
/// A point-in-time view of one routing value's application budget, for logging and
/// for the ops metrics that answer "are we actually using the key we have?".
/// </summary>
/// <param name="RoutingValue">Riot routing value, e.g. <c>europe</c> or <c>euw1</c>.</param>
/// <param name="Windows">The application windows enforced for it.</param>
/// <param name="PenaltyUntilUtc">When a 429 penalty on the application budget expires, if any.</param>
/// <param name="MethodBudgets">How many endpoint budgets have been learned for it.</param>
public sealed record RiotRateLimitSnapshot(
    string RoutingValue,
    IReadOnlyList<(int PermitLimit, TimeSpan Duration)> Windows,
    DateTimeOffset? PenaltyUntilUtc,
    int MethodBudgets);
