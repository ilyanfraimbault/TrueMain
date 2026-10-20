namespace TrueMain.ReadModels.Benchmarks;

/// <summary>
/// The form benchmark of one position (#2076): for every tier with data, how many laners'
/// games were sampled and the quartiles of the figures the desktop dashboard's form tiles
/// show. Like the pace benchmark, every tier is answered and the app picks the player's own
/// locally, so the rank it reads from the League client never leaves the machine.
/// </summary>
public sealed record FormBenchmarkResponse
{
    public string Position { get; init; } = string.Empty;

    /// <summary>The patches pooled, newest first; empty when nothing has been folded yet.</summary>
    public IReadOnlyList<string> Patches { get; init; } = [];

    /// <summary>The sample a tier needs before its quartiles are served.</summary>
    public int MinSamples { get; init; }

    /// <summary>Tiers in ladder order (Iron first); a tier with no sample at all is absent.</summary>
    public IReadOnlyList<FormBenchmarkTierReadModel> Tiers { get; init; } = [];
}

/// <summary>
/// One tier of a position. Every quartile is null under
/// <see cref="FormBenchmarkResponse.MinSamples"/>: the tile then shows no reference rather
/// than one drawn from a handful of games.
/// </summary>
public sealed record FormBenchmarkTierReadModel
{
    /// <summary>The ranked tier, as the <c>EloBracket</c> ladder spells it (e.g. <c>DIAMOND</c>).</summary>
    public string Tier { get; init; } = string.Empty;

    /// <summary>Laners' games counted (one per participant on the lane).</summary>
    public long Samples { get; init; }

    /// <summary><c>(kills + assists) / deaths</c>, deaths floored at one, per game.</summary>
    public PaceQuartilesReadModel? Kda { get; init; }

    /// <summary>Share of the team's kills the player took part in, 0 to 1, per game.</summary>
    public PaceQuartilesReadModel? KillParticipation { get; init; }

    /// <summary>Minions plus jungle monsters killed per minute of game.</summary>
    public PaceQuartilesReadModel? CsPerMinute { get; init; }

    /// <summary>Deaths per game.</summary>
    public PaceQuartilesReadModel? Deaths { get; init; }

    /// <summary>Vision score per minute of game.</summary>
    public PaceQuartilesReadModel? VisionPerMinute { get; init; }
}
