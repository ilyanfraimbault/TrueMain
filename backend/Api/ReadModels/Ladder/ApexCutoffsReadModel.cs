namespace TrueMain.ReadModels.Ladder;

/// <summary>
/// Payload behind <c>GET /ladder/{platformId}/apex-cutoffs</c> (#2174): the region's
/// Grandmaster and Challenger thresholds, one entry per measured UTC day, oldest first.
/// </summary>
public sealed class ApexCutoffsReadModel
{
    public IReadOnlyList<ApexCutoffReadModel> Entries { get; init; } = Array.Empty<ApexCutoffReadModel>();
}

/// <summary>
/// One day's apex thresholds. A tier is <c>null</c> when its league was not read that day
/// (a failed call, or the tier out of the ladder sync's scope).
/// </summary>
public sealed class ApexCutoffReadModel
{
    /// <summary>UTC calendar day, serialised as <c>YYYY-MM-DD</c>.</summary>
    public DateOnly DateUtc { get; init; }

    public int? GrandmasterMinLp { get; init; }

    public int? ChallengerMinLp { get; init; }
}
