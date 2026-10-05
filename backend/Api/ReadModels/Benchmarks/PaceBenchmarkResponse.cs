namespace TrueMain.ReadModels.Benchmarks;

/// <summary>
/// The pace benchmark of one position (#1912): for every tier with data, per whole minute,
/// how many laners were sampled and the quartiles of their cumulative CS and gold earned.
/// The desktop overlay picks the player's tier locally — the rank it reads from the League
/// client never leaves the machine — and divides by the minute to show a per-minute rate.
/// </summary>
public sealed record PaceBenchmarkResponse
{
    public string Position { get; init; } = string.Empty;

    /// <summary>The patches pooled, newest first; empty when nothing has been folded yet.</summary>
    public IReadOnlyList<string> Patches { get; init; } = [];

    /// <summary>The sample a minute needs before its quartiles are served.</summary>
    public int MinSamples { get; init; }

    /// <summary>Tiers in ladder order (Iron first); a tier with no sample at all is absent.</summary>
    public IReadOnlyList<PaceBenchmarkTierReadModel> Tiers { get; init; } = [];
}

public sealed record PaceBenchmarkTierReadModel
{
    /// <summary>The ranked tier, as the <c>EloBracket</c> ladder spells it (e.g. <c>DIAMOND</c>).</summary>
    public string Tier { get; init; } = string.Empty;

    /// <summary>Every minute with at least one sample, ascending.</summary>
    public IReadOnlyList<PaceBenchmarkMinuteReadModel> Minutes { get; init; } = [];
}

public sealed record PaceBenchmarkMinuteReadModel
{
    public int Minute { get; init; }

    /// <summary>Laners counted at this minute (one per participant whose game reached it).</summary>
    public long Samples { get; init; }

    /// <summary>Cumulative creep score (minions + jungle monsters); null under <see cref="PaceBenchmarkResponse.MinSamples"/>.</summary>
    public PaceQuartilesReadModel? Cs { get; init; }

    /// <summary>Cumulative gold earned, starting gold included; null under <see cref="PaceBenchmarkResponse.MinSamples"/>.</summary>
    public PaceQuartilesReadModel? GoldEarned { get; init; }
}

public sealed record PaceQuartilesReadModel
{
    public double P25 { get; init; }

    public double Median { get; init; }

    public double P75 { get; init; }
}
