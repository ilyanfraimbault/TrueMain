using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionItemContextQueryService
{
    /// <summary>
    /// The situational build context of a champion at a position (#1450): one verdict per
    /// item the slice's builds reach, read straight from
    /// <c>champion_item_context_verdicts</c> with no statistics on this side.
    ///
    /// <para>
    /// <paramref name="patch"/> is optional: omitted, the newest patch the champion has
    /// verdicts for is served, so a page that has not resolved its patch filter yet still
    /// gets an answer rather than an empty one. A slice the fold has not produced verdicts
    /// for returns an empty item list — a thin slice is not an error.
    /// </para>
    /// </summary>
    Task<ChampionItemContextResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        CancellationToken ct = default);
}
