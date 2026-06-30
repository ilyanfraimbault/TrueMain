using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public interface IMatchDetailQueryService
{
    /// <summary>
    /// Loads the full detail payload for a single match — all 10 participants
    /// with their build order, skill order, rune page and timeline-derived
    /// laning stats. <paramref name="nameTag"/> (<c>gameName-tagLine</c>) is
    /// validated and must resolve to a tracked account, but only scopes the
    /// route; the response covers every participant.
    ///
    /// Returns <c>null</c> when the name tag is malformed, no Riot account
    /// matches, or the match id is unknown / not one this account played in.
    /// </summary>
    Task<MatchDetailReadModel?> GetAsync(string nameTag, string matchId, CancellationToken ct);
}
