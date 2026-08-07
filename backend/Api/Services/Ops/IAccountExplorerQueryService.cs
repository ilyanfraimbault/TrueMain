using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

/// <summary>
/// Read path for the admin account explorer (#1032): everything the pipeline
/// knows about one Riot ID, in one read-model.
/// </summary>
public interface IAccountExplorerQueryService
{
    /// <summary>
    /// Traces one Riot ID through the pipeline. Never returns null: a Riot ID the
    /// pipeline has never seen is a populated read-model in the
    /// <c>NeverDiscovered</c> state, because "we have never seen this account" is
    /// an answer this page exists to give.
    /// </summary>
    /// <param name="gameName">Riot ID game name, already parsed out of the route segment.</param>
    /// <param name="tagLine">Riot ID tag line, already parsed out of the route segment.</param>
    /// <param name="platformId">
    /// Canonical platform id (e.g. "EUW1") to restrict the search to, or null to
    /// search every region. The controller validates it; anything reaching here is
    /// either canonical or null.
    /// </param>
    /// <param name="ct">Request cancellation token.</param>
    Task<AccountExplorerReadModel> GetAsync(
        string gameName,
        string tagLine,
        string? platformId,
        CancellationToken ct);
}
