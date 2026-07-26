using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

public interface IChampionMainsComparisonQueryService
{
    /// <summary>
    /// Compares one Riot account's games on a champion against the champion's
    /// mains. <paramref name="account"/> and <paramref name="target"/> accept a
    /// Riot ID in either the typed <c>Name#TAG</c> form or the URL slug
    /// <c>Name-TAG</c>; a null/blank <paramref name="target"/> compares against
    /// the aggregate of every tracked main.
    ///
    /// A non-blank <paramref name="target"/> resolves to any account we hold —
    /// it is deliberately not required to be flagged a main of the champion, so
    /// a caller can measure against a specific rival. Only the pool branch
    /// filters on <c>IsMain</c>.
    ///
    /// Never throws for unknown input: an unresolvable
    /// <paramref name="account"/> yields
    /// <see cref="ReadModels.Champions.ChampionComparisonStatus.UnknownAccount"/>
    /// with no columns, and an unresolvable <paramref name="target"/> yields
    /// <see cref="ReadModels.Champions.ChampionComparisonStatus.UnknownTarget"/>
    /// with the player's column still populated.
    /// </summary>
    Task<ChampionMainsComparisonResponse> GetAsync(
        int championId,
        string? account,
        string? target,
        string? position,
        string? patch,
        CancellationToken ct);
}
