namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Trio completions for an already-chosen duo (#922): "you play this champion,
/// your friend plays that one — what should the third pick?".
///
/// Unlike the duo slice this is NOT pre-aggregated. The pair space is bounded by
/// (champion × lane × partner × lane), but the triple space multiplies that by a
/// third champion and lane, which is both far larger and almost entirely empty —
/// most triples are never played, and the few that are carry single-digit samples.
/// Storing it would cost orders of magnitude more rows than it could ever answer,
/// so a trio is computed on demand from <c>match_participants</c>, narrowed first
/// to the games where the chosen pair actually played together. That keeps the
/// query bounded by the pair's own game count rather than by the champion's.
///
/// The consequence is a scope difference worth surfacing: the pair figures here are
/// counted over the currently-retained matches, while the duo endpoint reads an
/// aggregate that also holds frozen older patches. The two therefore describe the
/// same pairing over different windows and their game counts will not match — which
/// is why <see cref="PairGames"/> is returned explicitly instead of being taken
/// from the duo response.
/// </summary>
public sealed record ChampionTrioSynergiesResponse
{
    public int ChampionId { get; init; }

    public string Position { get; init; } = string.Empty;

    public int PartnerChampionId { get; init; }

    public string PartnerPosition { get; init; } = string.Empty;

    /// <summary>
    /// Resolved patch (<c>major.minor</c>), or <see langword="null"/> for every
    /// patch still inside the retention window.
    /// </summary>
    public string? Patch { get; init; }

    /// <summary>
    /// Minimum games a trio needed to appear in <see cref="Completions"/>. Echoed
    /// so a caller can say "no third pick has been played with this duo often
    /// enough yet" rather than "this duo has no good third".
    /// </summary>
    public int MinGames { get; init; }

    /// <summary>
    /// Games the duo itself played together in the live window. This is the ceiling
    /// every completion's sample is drawn from: a duo with 30 games can never offer
    /// a third pick with more than 30.
    /// </summary>
    public int PairGames { get; init; }

    public int PairWins { get; init; }

    /// <summary>
    /// <see cref="PairWins"/> / <see cref="PairGames"/>, or 0 when the duo has no
    /// recorded games in the window.
    /// </summary>
    public double PairWinRate { get; init; }

    /// <summary>
    /// One entry per third pick above the floor, ordered by
    /// <see cref="ChampionTrioSynergyEntry.Synergy"/> descending. Empty whenever the
    /// duo is too rare to support a third dimension — the expected case, not an error.
    /// </summary>
    public IReadOnlyList<ChampionTrioSynergyEntry> Completions { get; init; } = [];
}

/// <summary>A single third pick's line for the chosen duo.</summary>
public sealed record ChampionTrioSynergyEntry
{
    public int ChampionId { get; init; }

    public string Position { get; init; } = string.Empty;

    /// <summary>Games the full trio played together.</summary>
    public int Games { get; init; }

    public int Wins { get; init; }

    public double WinRate { get; init; }

    /// <summary>
    /// Games behind the third pick's marginal win rate — read from the same
    /// baseline table the duo slice uses, so a trio and a duo judge a champion
    /// against the same reference.
    /// </summary>
    public int BaselineGames { get; init; }

    public double BaselineWinRate { get; init; }

    /// <summary>
    /// What the trio should have won given all three marginals: the queried
    /// champion's own rate plus both allies', combined in log-odds space against
    /// the cohort reference point.
    /// </summary>
    public double ExpectedWinRate { get; init; }

    /// <summary><see cref="WinRate"/> − <see cref="ExpectedWinRate"/>.</summary>
    public double Synergy { get; init; }
}
