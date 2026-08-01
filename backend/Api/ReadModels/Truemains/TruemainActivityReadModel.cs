namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// Activity-grid payload behind <c>GET /truemains/{nameTag}/activity</c> (#927):
/// the four granularities the profile heatmap can switch between, resolved in a
/// single request.
/// </summary>
/// <remarks>
/// All four series ship together on purpose. Three of them
/// (<see cref="Game"/> / <see cref="Day"/> / <see cref="Week"/>) are three
/// foldings of the *same* participant rows, so computing them from one snapshot
/// is both cheaper than four round trips and the only way to guarantee that
/// flipping the mode switch cannot show two different answers for the same
/// afternoon. <see cref="Patch"/> reads a different table with different
/// retention — see <see cref="TruemainActivitySeriesReadModel.Source"/> — which
/// is exactly why every series carries its own scope, source and coverage
/// instead of the client assuming they are comparable.
/// </remarks>
public sealed class TruemainActivityReadModel
{
    /// <summary>One cell per game over the most recent retained games.</summary>
    public required TruemainActivitySeriesReadModel Game { get; init; }

    /// <summary>One cell per UTC calendar day.</summary>
    public required TruemainActivitySeriesReadModel Day { get; init; }

    /// <summary>One cell per ISO week (Monday 00:00 UTC).</summary>
    public required TruemainActivitySeriesReadModel Week { get; init; }

    /// <summary>One cell per patch, on the player's signature champion only.</summary>
    public required TruemainActivitySeriesReadModel Patch { get; init; }
}

/// <summary>
/// One granularity of the activity grid, with everything the UI needs to state
/// what it is looking at. The metadata is not decoration: the modes read
/// different tables with different retention and different scopes, so a client
/// that rendered four grids as if they were the same measurement would be
/// silently wrong.
/// </summary>
public sealed class TruemainActivitySeriesReadModel
{
    /// <summary>
    /// Granularity id — one of <see cref="TruemainActivityKinds.GameMode"/>,
    /// <see cref="TruemainActivityKinds.DayMode"/>,
    /// <see cref="TruemainActivityKinds.WeekMode"/>,
    /// <see cref="TruemainActivityKinds.PatchMode"/>.
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// Where the numbers come from:
    /// <see cref="TruemainActivityKinds.MatchesSource"/> (live
    /// <c>match_participants</c>, deleted by retention past the last couple of
    /// patches) or <see cref="TruemainActivityKinds.AggregatesSource"/>
    /// (<c>champion_aggregate_scopes</c>, frozen forever — #466).
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Which games are counted:
    /// <see cref="TruemainActivityKinds.AllChampionsScope"/> or
    /// <see cref="TruemainActivityKinds.ChampionScope"/> (then
    /// <see cref="ChampionId"/> says which). The patch series is champion-scoped
    /// because the per-account, per-champion aggregate is the only place a full
    /// patch history survives — so its totals are deliberately *not* the same
    /// population as the other three, and the UI has to say so.
    /// </summary>
    public required string Scope { get; init; }

    /// <summary>
    /// The champion the series is scoped to, or <see langword="null"/> for an
    /// all-champions series. Also <see langword="null"/> on a champion-scoped
    /// series when the player has no classified main to scope to — the series is
    /// then empty rather than silently widened to every champion.
    /// </summary>
    public int? ChampionId { get; init; }

    /// <summary>
    /// <see langword="true"/> when this series can only ever see the retained
    /// match window (<c>MatchDataRetention:RetainedPatchCount</c>, ~2 patches),
    /// so an absent period may mean "deleted", not "not played". The patch series
    /// is the one that reads <see langword="false"/>.
    /// </summary>
    public required bool RetentionBounded { get; init; }

    /// <summary>
    /// Start of the period the series actually speaks for, or
    /// <see langword="null"/> when it holds no data. For the match-sourced series
    /// this is the later of "the requested window" and "the oldest game still on
    /// disk": periods before it are dropped rather than rendered as empty, because
    /// an erased period is not an idle one. <see langword="null"/> on the patch
    /// series, whose extent is a patch list rather than a date range.
    /// </summary>
    public DateTime? CoverageFromUtc { get; init; }

    /// <summary>End of the period the series speaks for; see <see cref="CoverageFromUtc"/>.</summary>
    public DateTime? CoverageToUtc { get; init; }

    /// <summary>
    /// The cells, oldest first. Within <see cref="CoverageFromUtc"/> ..
    /// <see cref="CoverageToUtc"/> the calendar series emit a cell per period
    /// including the empty ones — an idle day is a fact worth drawing — while the
    /// game and patch series only emit cells that have data by construction.
    /// </summary>
    public required IReadOnlyList<TruemainActivityBucketReadModel> Buckets { get; init; }

    /// <summary>Games across the whole series.</summary>
    public required int Games { get; init; }

    /// <summary>Wins across the whole series.</summary>
    public required int Wins { get; init; }

    /// <summary>
    /// Win rate across the series, or <see langword="null"/> when
    /// <see cref="Games"/> is 0. Never 0 for an empty series — "measured at 0%"
    /// and "nothing measured" are different facts.
    /// </summary>
    public double? WinRate { get; init; }
}

/// <summary>
/// One cell of the activity grid.
/// </summary>
public sealed class TruemainActivityBucketReadModel
{
    /// <summary>
    /// Stable cell identity: a match id (game series), <c>yyyy-MM-dd</c> of the
    /// UTC day (day series), <c>yyyy-MM-dd</c> of the ISO week's Monday (week
    /// series), or the <c>major.minor</c> patch (patch series). Unique within the
    /// series, so the client can key on it.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Start of the period the cell covers, or <see langword="null"/> for the
    /// patch series (a patch has no stored start date — only its scopes' last
    /// game is recorded).
    /// </summary>
    public DateTime? StartUtc { get; init; }

    /// <summary>Games in the cell. <c>0</c> is a real answer: an idle day.</summary>
    public required int Games { get; init; }

    /// <summary>Wins in the cell.</summary>
    public required int Wins { get; init; }

    /// <summary>
    /// Win rate in the cell, or <see langword="null"/> when <see cref="Games"/>
    /// is 0. This is the wire-level distinction between "played and lost
    /// everything" (0) and "did not play" (null); the two must not render alike.
    /// </summary>
    public double? WinRate { get; init; }

    /// <summary>
    /// Champion played, on the game series only — a per-game cell is one game, so
    /// it has exactly one. <see langword="null"/> on every aggregated series,
    /// where a cell can span several champions (or, on the patch series, is
    /// already scoped to one by
    /// <see cref="TruemainActivitySeriesReadModel.ChampionId"/>).
    /// </summary>
    public int? ChampionId { get; init; }
}

/// <summary>
/// Wire values for the activity series' <c>mode</c> / <c>source</c> / <c>scope</c>
/// discriminators. Named constants rather than enums so the JSON stays the plain
/// lowercase strings the rest of the API uses, with no converter to keep in sync.
/// </summary>
public static class TruemainActivityKinds
{
    /// <summary>One cell per game.</summary>
    public const string GameMode = "game";

    /// <summary>One cell per UTC calendar day.</summary>
    public const string DayMode = "day";

    /// <summary>One cell per ISO week.</summary>
    public const string WeekMode = "week";

    /// <summary>One cell per patch.</summary>
    public const string PatchMode = "patch";

    /// <summary>Live <c>match_participants</c> rows; bounded by match retention.</summary>
    public const string MatchesSource = "matches";

    /// <summary>Frozen <c>champion_aggregate_scopes</c> rows; unbounded history.</summary>
    public const string AggregatesSource = "aggregates";

    /// <summary>Every champion the player played.</summary>
    public const string AllChampionsScope = "allChampions";

    /// <summary>A single champion (see <c>championId</c>).</summary>
    public const string ChampionScope = "champion";
}
