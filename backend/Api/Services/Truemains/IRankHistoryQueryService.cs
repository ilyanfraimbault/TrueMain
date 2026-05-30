using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public interface IRankHistoryQueryService
{
    /// <summary>
    /// Returns the rank-history payload for <paramref name="nameTag"/>
    /// (<c>gameName-tagLine</c>) covering the last <paramref name="days"/>
    /// days. Returns <c>null</c> when the name tag is malformed or no Riot
    /// account matches — the controller maps that to 404. An empty entries
    /// list is a valid response when the account exists but has no
    /// snapshots in the window.
    /// </summary>
    Task<RankHistoryReadModel?> GetAsync(string nameTag, int days, CancellationToken ct);
}
