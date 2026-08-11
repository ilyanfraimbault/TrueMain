namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Champion synergies read model (#922) — "you play this champion at this lane,
/// who should your friends play?". Lists the teammates a tracked player on this
/// champion actually won more (or less) with than their individual win rates
/// predicted, read from the pre-aggregated <c>champion_synergy_stats</c> table.
///
/// The ranking value is <see cref="ChampionSynergyEntry.Synergy"/>, not
/// <see cref="ChampionSynergyEntry.WinRate"/>: a raw pair win rate mostly restates
/// how strong the two champions are on their own, which is what the tier list is
/// for. Every entry carries its own <see cref="ChampionSynergyEntry.Games"/> so a
/// caller can show what a number is built on, and only pairs above the
/// <see cref="MinGames"/> floor are returned at all.
/// </summary>
public sealed record ChampionSynergiesResponse
{
    public int ChampionId { get; init; }

    /// <summary>Lane the champion is played in for this slice.</summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>
    /// Resolved patch (<c>major.minor</c>), or <see langword="null"/> when the
    /// slice spans every patch with data.
    /// </summary>
    public string? Patch { get; init; }

    /// <summary>
    /// The partner lane the caller narrowed to, or <see langword="null"/> when
    /// every lane is included.
    /// </summary>
    public string? PartnerPosition { get; init; }

    /// <summary>
    /// Minimum games a pair needed to appear in <see cref="Partners"/>. Echoed so
    /// the caller can explain an empty list instead of implying "no synergy".
    /// </summary>
    public int MinGames { get; init; }

    /// <summary>
    /// Games behind <see cref="ChampionWinRate"/> — the tracked-player sample this
    /// champion's own baseline rests on. Zero means the champion has no recorded
    /// games in the scope, so <see cref="Partners"/> is necessarily empty.
    /// </summary>
    public int ChampionGames { get; init; }

    /// <summary>
    /// The champion's own win rate at this lane over the scoped games. One of the
    /// two inputs to every entry's expected win rate.
    /// </summary>
    public double ChampionWinRate { get; init; }

    /// <summary>
    /// Win rate of the whole tracked cohort in the scope — the reference point ally
    /// win rates are measured against. Published rather than hidden because it is
    /// what makes the expected values reproducible: a partner sitting exactly here
    /// contributes nothing to the expectation.
    /// </summary>
    public double CohortWinRate { get; init; }

    /// <summary>
    /// One entry per qualifying teammate, ordered by
    /// <see cref="ChampionSynergyEntry.Synergy"/> descending (best partner first).
    /// </summary>
    public IReadOnlyList<ChampionSynergyEntry> Partners { get; init; } = [];
}

/// <summary>A single teammate's synergy line.</summary>
public sealed record ChampionSynergyEntry
{
    public int PartnerChampionId { get; init; }

    /// <summary>Lane the partner played — always different from the champion's.</summary>
    public string PartnerPosition { get; init; } = string.Empty;

    /// <summary>Games the two were on the same team, at these two lanes.</summary>
    public int Games { get; init; }

    public int Wins { get; init; }

    /// <summary>
    /// <see cref="Wins"/> / <see cref="Games"/>. <see cref="Games"/> is always at
    /// least the response's <c>minGames</c> floor, so this never divides by zero.
    /// </summary>
    public double WinRate { get; init; }

    /// <summary>
    /// Share of the champion's own games this partner was on the team for — the
    /// quantity <see cref="TrueMain.Options.ChampionsListOptions.MinSynergyPlayRate"/>
    /// floors, returned so a caller can say "you have this teammate in 3% of your
    /// games" instead of leaving a bare count to mean whatever the reader assumes.
    /// </summary>
    public double PlayRate { get; init; }

    /// <summary>
    /// Games behind <see cref="PartnerBaselineWinRate"/> — always at least
    /// <see cref="Games"/>, since it counts every game this champion was a tracked
    /// player's teammate, not only the ones alongside the queried champion.
    /// </summary>
    public int PartnerBaselineGames { get; init; }

    /// <summary>
    /// The partner's marginal win rate: how often a tracked player's team won with
    /// this champion at this lane on it, across all its partners. This is
    /// deliberately not the partner's own solo win rate as a tracked main — that
    /// population is a specialist on their champion, while a partner is whoever
    /// showed up, and mixing the two biases every expectation upward.
    /// </summary>
    public double PartnerBaselineWinRate { get; init; }

    /// <summary>
    /// What the pair should have won given the two marginals and the cohort
    /// reference point, combined in log-odds space (<c>Core.Lol.Synergy.SynergyMath</c>).
    /// </summary>
    public double ExpectedWinRate { get; init; }

    /// <summary>
    /// <see cref="WinRate"/> − <see cref="ExpectedWinRate"/>. Positive means the
    /// pairing added something beyond the two champions being individually good.
    /// </summary>
    public double Synergy { get; init; }
}
