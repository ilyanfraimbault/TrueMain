using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public interface IMatchSummariesQueryService
{
    /// <summary>
    /// Returns the page of recent matches for the player identified by
    /// <paramref name="nameTag"/> (<c>gameName-tagLine</c>), ordered by game
    /// start time descending. <paramref name="before"/> is the cursor — only
    /// matches strictly older than this timestamp are returned. Returns
    /// <c>null</c> when the name tag is malformed or no Riot account matches.
    /// </summary>
    Task<MatchSummariesResponse?> GetAsync(
        string nameTag,
        int limit,
        DateTime? before,
        CancellationToken ct);
}
