namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// Activity-grid payload behind <c>GET /truemains/{nameTag}/activity</c>
/// (#927, reshaped in #1473).
/// </summary>
/// <remarks>
/// <para>
/// <b>The grid's unit is the day.</b> The three series are not three units — they
/// are three <em>windows</em> over the same participant rows, and two of them
/// (<see cref="Patch"/> and <see cref="Week"/>) draw exactly one cell per UTC
/// calendar day inside their window, played or not. Only <see cref="Day"/>, the
/// narrowest window, falls back to one cell per game: a single day has no days to
/// draw.
/// </para>
/// <para>
/// The earlier shape had four series with four different units — per game, per
/// day, per ISO week, per patch — which meant the patch view answered "how did
/// each patch go" and could never answer "which days of this patch did I play".
/// It also read a second table (the frozen per-champion aggregates) to do it, so
/// its total was a different population from the one directly above it.
/// Everything here now reads live <c>match_participants</c> for every champion,
/// which is why the series no longer carry a per-series <c>source</c> /
/// <c>scope</c> discriminator: there is only one of each.
/// </para>
/// <para>
/// All three still ship in a single response — they are foldings of one snapshot
/// of the same rows, so flipping the window cannot show two different answers for
/// the same afternoon.
/// </para>
/// </remarks>
public sealed class TruemainActivityReadModel
{
    /// <summary>
    /// One cell per UTC day of the current patch, from the day the patch's first
    /// game was played through today. The default view.
    /// </summary>
    public required TruemainActivitySeriesReadModel Patch { get; init; }

    /// <summary>One cell per UTC day over the last seven days, today included.</summary>
    public required TruemainActivitySeriesReadModel Week { get; init; }

    /// <summary>One cell per game played on the current UTC day; empty on a rest day.</summary>
    public required TruemainActivitySeriesReadModel Day { get; init; }
}

/// <summary>
/// One window of the activity grid.
/// </summary>
public sealed class TruemainActivitySeriesReadModel
{
    /// <summary>
    /// Window id — one of <see cref="TruemainActivityKinds.PatchMode"/>,
    /// <see cref="TruemainActivityKinds.WeekMode"/>,
    /// <see cref="TruemainActivityKinds.DayMode"/>.
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// The <c>major.minor</c> patch the window covers, on the patch series only;
    /// <see langword="null"/> elsewhere, and also on the patch series when no
    /// tracked match has a parseable patch yet (a fresh database).
    /// </summary>
    public string? Patch { get; init; }

    /// <summary>
    /// Start of the window the series speaks for, or <see langword="null"/> when
    /// it holds no cell at all. Read off the emitted cells rather than computed a
    /// second time, so the claim and the squares behind it cannot drift.
    /// </summary>
    public DateTime? CoverageFromUtc { get; init; }

    /// <summary>End of the window the series speaks for; see <see cref="CoverageFromUtc"/>.</summary>
    public DateTime? CoverageToUtc { get; init; }

    /// <summary>
    /// The cells, oldest first. The two calendar windows emit a cell for
    /// <em>every</em> day they span, including the days with no games — an idle
    /// day is a fact worth drawing, and the run of them between two sessions is
    /// what makes a busy stretch legible.
    /// </summary>
    public required IReadOnlyList<TruemainActivityBucketReadModel> Buckets { get; init; }

    /// <summary>Games across the whole window.</summary>
    public required int Games { get; init; }

    /// <summary>Wins across the whole window.</summary>
    public required int Wins { get; init; }

    /// <summary>
    /// Win rate across the window, or <see langword="null"/> when
    /// <see cref="Games"/> is 0. Never 0 for an empty window — "measured at 0%"
    /// and "nothing measured" are different facts.
    /// </summary>
    public double? WinRate { get; init; }
}

/// <summary>
/// One cell of the activity grid: a UTC day on the patch and week windows, a
/// single game on the day window.
/// </summary>
public sealed class TruemainActivityBucketReadModel
{
    /// <summary>
    /// Stable cell identity: <c>yyyy-MM-dd</c> of the UTC day on a calendar
    /// window, the match id on the day window. Unique within the series, so the
    /// client can key on it.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Start of what the cell covers: 00:00 UTC of its day, or the game's start
    /// instant. Never null — every window this endpoint emits is dated.
    /// </summary>
    public required DateTime StartUtc { get; init; }

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
    /// Champion played, on the day window only — a per-game cell is one game, so
    /// it has exactly one. <see langword="null"/> on a calendar cell, which can
    /// span several champions.
    /// </summary>
    public int? ChampionId { get; init; }
}

/// <summary>
/// Wire values for the activity series' <c>mode</c> discriminator. Named
/// constants rather than an enum so the JSON stays the plain lowercase strings
/// the rest of the API uses, with no converter to keep in sync.
/// </summary>
public static class TruemainActivityKinds
{
    /// <summary>Every UTC day of the current patch.</summary>
    public const string PatchMode = "patch";

    /// <summary>The last seven UTC days.</summary>
    public const string WeekMode = "week";

    /// <summary>Today's games, one cell each.</summary>
    public const string DayMode = "day";
}
