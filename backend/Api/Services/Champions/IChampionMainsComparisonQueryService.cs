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
    /// </summary>
    Task<ChampionMainsComparisonResponse> GetAsync(
        int championId,
        string? account,
        string? target,
        string? position,
        string? patch,
        CancellationToken ct);
}
