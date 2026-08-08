namespace TrueMain.ReadModels.Ops;

/// <summary>
/// Whether the patches the public surfaces read are actually servable (#1033).
///
/// <para>
/// Every public surface is patch-scoped — the champion directory, the tier list, the
/// trend charts, the patch diff — and until this view the only way to know that a new
/// patch was still thin was to browse those pages and eyeball the sample counts. The
/// question it answers is one sentence long: <em>can the site serve this patch, and if
/// not, is that because nothing has aggregated it yet or because it genuinely has too
/// few games?</em> Those two call for opposite reactions, which is why they are separate
/// verdicts rather than one low number.
/// </para>
/// </summary>
public sealed record PatchCoverageReadModel
{
    /// <summary>The ranked queue every count here is scoped to (<c>MainAnalysis:QueueId</c>).</summary>
    public int QueueId { get; init; }

    /// <summary>
    /// The games floor the public reads apply, echoed from
    /// <c>ChampionsList:MinSampleGames</c> rather than re-declared, so this page can
    /// never judge against a bar the site does not enforce.
    /// </summary>
    public int MinSampleGames { get; init; }

    /// <summary>The floor stated in words, so the verdict never rests on a bare number.</summary>
    public string FloorNote { get; init; } = string.Empty;

    /// <summary>
    /// The patch the public reads resolve to today — the newest one holding aggregate
    /// rows, which is exactly how <c>ChampionAggregateScopeResolver</c> picks it. Null
    /// when nothing has been aggregated at all.
    /// </summary>
    public string? CurrentPatch { get; init; }

    /// <summary>
    /// The current patch's verdict, hoisted so the page opens on the answer.
    /// See <see cref="PatchCoverageRowReadModel.Verdict"/>.
    /// </summary>
    public string Verdict { get; init; } = "unknown";

    /// <summary><c>green</c> | <c>amber</c> | <c>red</c> | <c>unknown</c> for the current patch.</summary>
    public string Status { get; init; } = "unknown";

    /// <summary>The current patch's verdict in a sentence, with the floor in it.</summary>
    public string Headline { get; init; } = string.Empty;

    /// <summary>
    /// Why no verdict could be given, when one of the two measurements the page rests on
    /// failed. Set if and only if <see cref="Verdict"/> is <c>unknown</c> for that reason:
    /// without the coverage rollup, "thin" and "not aggregated" are indistinguishable, and
    /// guessing between them is worse than saying nothing.
    /// </summary>
    public string? UnknownReason { get; init; }

    /// <summary>Covered patches, newest first.</summary>
    public IReadOnlyList<PatchCoverageRowReadModel> Patches { get; init; } = [];

    /// <summary>Which tables were read and what that costs, on the panel rather than in a comment.</summary>
    public string SourceNote { get; init; } = string.Empty;

    /// <summary>When the view was computed. Every age on it is relative to this, not to the browser's clock.</summary>
    public DateTime EvaluatedAtUtc { get; init; }
}

/// <summary>One patch's ingestion, aggregate coverage and per-fold state.</summary>
public sealed record PatchCoverageRowReadModel
{
    /// <summary>Normalised "MAJOR.MINOR" patch.</summary>
    public string Patch { get; init; } = string.Empty;

    /// <summary>True for the patch the public reads currently resolve to.</summary>
    public bool IsCurrent { get; init; }

    /// <summary>
    /// First-match-wins verdict:
    /// <list type="bullet">
    ///   <item><c>servable</c> — enough lines clear the floor for the patch-scoped surfaces to mean something;</item>
    ///   <item><c>notAggregated</c> — matches were ingested but no aggregate row exists yet. <b>Wait, or check the fold</b>;</item>
    ///   <item><c>thin</c> — aggregated, and still short of the bar. <b>The patch genuinely lacks games</b>;</item>
    ///   <item><c>unknown</c> — nothing ingested and nothing aggregated, so there is no reading to give.</item>
    /// </list>
    /// <c>notAggregated</c> and <c>thin</c> are kept apart on purpose: they are the two
    /// causes of the same low number and they call for opposite reactions.
    /// </summary>
    public string Verdict { get; init; } = "unknown";

    /// <summary><c>green</c> | <c>amber</c> | <c>red</c> | <c>unknown</c>.</summary>
    public string Status { get; init; } = "unknown";

    /// <summary>The verdict in a sentence, naming the floor and the bar it was judged against.</summary>
    public string Headline { get; init; } = string.Empty;

    /// <summary>Matches ingested on this patch, on the scoped queue.</summary>
    public long Matches { get; init; }

    /// <summary>Participant rows behind those matches. Retention prunes these while the aggregates survive (#466).</summary>
    public long Participants { get; init; }

    /// <summary>Earliest game start on the patch, or null when no match survives retention.</summary>
    public DateTime? FirstGameStartUtc { get; init; }

    /// <summary>Latest game start on the patch.</summary>
    public DateTime? LastGameStartUtc { get; init; }

    /// <summary>Matches and participants by game date (UTC), oldest first — how the patch filled.</summary>
    public IReadOnlyList<PatchCoverageDayReadModel> Daily { get; init; } = [];

    /// <summary>
    /// <c>(champion, lane)</c> pairs holding at least one aggregate row on this patch —
    /// the same grain and the same queue filter the champion directory groups by, so the
    /// two can never disagree. Lane-less scope rows are excluded exactly as the ranked
    /// directory excludes them.
    /// </summary>
    public long Lines { get; init; }

    /// <summary>Of those, the ones whose summed games reach <see cref="PatchCoverageReadModel.MinSampleGames"/>.</summary>
    public long LinesPastFloor { get; init; }

    /// <summary>Distinct champions with at least one line on this patch.</summary>
    public long Champions { get; init; }

    /// <summary>Distinct champions with at least one line past the floor.</summary>
    public long ChampionsPastFloor { get; init; }

    /// <summary>
    /// The bar <see cref="LinesPastFloor"/> was judged against, or null when the patch was
    /// not judged. Derived from the median of the comparable patches, or from
    /// <c>PatchCoverage:ServableLinesMinimum</c> when there is no comparable patch.
    /// </summary>
    public double? ServableLinesBar { get; init; }

    /// <summary>Where that bar came from, in words — a number with no provenance is not an answer.</summary>
    public string? ServableLinesBarNote { get; init; }

    /// <summary>Total lines below the floor, whether or not they all fit in <see cref="BelowFloor"/>.</summary>
    public long BelowFloorCount { get; init; }

    /// <summary>
    /// The below-floor lines, <b>closest to the floor first</b>, capped by
    /// <c>PatchCoverage:ThinLineLimit</c>. Ordered that way because the question a thin
    /// patch raises is "how far off is it", and the lines about to clear answer it; the
    /// long tail of one-game off-role picks never will.
    /// </summary>
    public IReadOnlyList<PatchThinLineReadModel> BelowFloor { get; init; } = [];

    /// <summary>Per-fold coverage and freshness on this patch.</summary>
    public IReadOnlyList<PatchFoldCoverageReadModel> Folds { get; init; } = [];
}

/// <summary>One game date's ingestion on a patch.</summary>
public sealed record PatchCoverageDayReadModel
{
    /// <summary>Game date in UTC, ISO "yyyy-MM-dd".</summary>
    public string Date { get; init; } = string.Empty;

    public long Matches { get; init; }

    public long Participants { get; init; }
}

/// <summary>A <c>(champion, lane)</c> line that has games but not enough of them.</summary>
public sealed record PatchThinLineReadModel
{
    /// <summary>Riot champion id (the admin resolves names from static data).</summary>
    public int ChampionId { get; init; }

    /// <summary>Lane the line is scoped to (TOP/JUNGLE/MIDDLE/BOTTOM/UTILITY).</summary>
    public string Position { get; init; } = string.Empty;

    /// <summary>Games summed across the patch's scope rows for this line.</summary>
    public long Games { get; init; }

    /// <summary>Games still missing before the line clears the floor.</summary>
    public long GamesToFloor { get; init; }
}

/// <summary>
/// One aggregation fold's state on one patch: how much it has produced, how fresh that
/// is, and — for the folds that cannot be backfilled — whether the patch is even in
/// scope for it.
/// </summary>
public sealed record PatchFoldCoverageReadModel
{
    /// <summary>Stable key (<c>builds</c>, <c>matchups</c>, <c>bans</c>, …).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Human label for the fold.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// False when this patch predates the fold entirely. Raw match payloads are not kept,
    /// so a fold that shipped mid-corpus can never be backfilled (#920 bans, #957
    /// per-opponent power spikes): its rows on older patches are absent by construction,
    /// not missing. A zero there would read as "the fold is broken", which is the one
    /// thing it is not, so every count on such a row is null and
    /// <see cref="NotMeasuredNote"/> says so instead.
    /// </summary>
    public bool Measured { get; init; } = true;

    /// <summary>The oldest patch the fold has any row on, or null when it has none at all.</summary>
    public string? FirstMeasuredPatch { get; init; }

    /// <summary>Set if and only if <see cref="Measured"/> is false: "not measured before &lt;patch&gt;".</summary>
    public string? NotMeasuredNote { get; init; }

    /// <summary>Aggregate rows on this patch, or null when the patch is out of the fold's scope.</summary>
    public long? Rows { get; init; }

    /// <summary>Distinct champions on this patch, or null when out of scope.</summary>
    public long? Champions { get; init; }

    /// <summary>Newest write on this patch, or null when there is none.</summary>
    public DateTime? LastAggregatedAtUtc { get; init; }

    /// <summary>Hours since that write, judged against <c>DataQualityDetectors:AggregationStale*Hours</c>.</summary>
    public double? AgeHours { get; init; }

    /// <summary><c>green</c> | <c>amber</c> | <c>red</c> | <c>unknown</c> for that age.</summary>
    public string Status { get; init; } = "unknown";

    /// <summary>
    /// Matches on this patch the fold has not folded yet, from the per-match flag it
    /// advances. Null for the folds that carry no such flag (builds and mains are
    /// replace-by-scope per account, not per match), where a backlog is not expressible
    /// as a match count.
    /// </summary>
    public long? PendingMatches { get; init; }

    /// <summary>What this fold feeds and how to read its numbers.</summary>
    public string? Note { get; init; }
}
