namespace TrueMain.ReadModels.Champions;

/// <summary>
/// Full result of <see cref="Services.Champions.IChampionSummariesQueryService.GetAllSummariesAsync"/>
/// (#972): the ranked directory rows plus the totals computed from the whole
/// resolved patch, before the per-row filters (position required, min-sample
/// floor) that shape <see cref="Summaries"/> are applied. This is the object
/// cached by the query service, so every consumer of the same patch/elo slice
/// shares one total-games computation.
/// </summary>
public sealed record ChampionSummariesResult
{
    /// <summary>
    /// The resolved patch this result was computed for — the requested patch
    /// when non-null and canonical, otherwise the global latest patch found in
    /// the aggregate table. Empty when no patch could be resolved at all (no
    /// aggregate data exists yet for the queue).
    /// </summary>
    public string PatchVersion { get; init; } = string.Empty;

    /// <summary>
    /// True sum of <c>Games</c> across every <c>champion_aggregate_scopes</c>
    /// row that matched the queue/patch/elo filter — including rows below the
    /// min-sample floor and rows with no <c>Position</c>, both of which are
    /// excluded from <see cref="Summaries"/>. This is "games we actually
    /// aggregated this patch", not "games behind the ranked rows" — see #972.
    /// </summary>
    public long TotalGames { get; init; }

    /// <summary>The ranked, tiered <c>(champion, position)</c> rows — same shape <c>GET /champions</c> has always returned.</summary>
    public IReadOnlyList<ChampionSummaryReadModel> Summaries { get; init; } = [];
}
