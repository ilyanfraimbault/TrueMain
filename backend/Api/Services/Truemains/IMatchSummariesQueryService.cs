using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public interface IMatchSummariesQueryService
{
    /// <summary>
    /// Returns the page of matches for the player identified by
    /// <paramref name="nameTag"/> (<c>gameName-tagLine</c>), ordered by game
    /// start time descending. Page numbers are 1-indexed; values outside the
    /// valid range are clamped. Returns <c>null</c> when the name tag is
    /// malformed or no Riot account matches.
    /// </summary>
    Task<MatchSummariesResponse?> GetAsync(
        string nameTag,
        int page,
        int pageSize,
        CancellationToken ct);
}
