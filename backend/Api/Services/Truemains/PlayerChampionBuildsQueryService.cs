using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.Services.Truemains;

public sealed class PlayerChampionBuildsQueryService(
    TrueMainDbContext db,
    IChampionBuildsQueryService buildsQueryService) : IPlayerChampionBuildsQueryService
{
    /// <summary>
    /// Minimum games the player must have on the champion (at the resolved
    /// patch + position) for a build payload to be returned. Below this the
    /// service yields <see langword="null"/> so the page shows a "not enough
    /// games" notice instead of a build inferred from one or two matches.
    /// Five is the smallest sample where a dominant build path starts to mean
    /// something rather than echoing a single game.
    /// </summary>
    public const int MinPlayerGames = 5;

    public async Task<ChampionResponse?> GetAsync(
        string nameTag,
        int championId,
        string? patch,
        string? position,
        CancellationToken ct)
    {
        if (!NameTagParser.TryParse(nameTag, out var parsed))
        {
            return null;
        }

        // Resolve the name tag the same way the profile + matches endpoints do
        // (most-recently-active row on a cross-region collision) so all three
        // routes can never disagree about which account a name tag means.
        var account = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.GameName == parsed.GameName && a.TagLine == parsed.TagLine)
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .Select(a => new { a.Id, a.PlatformId })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return null;
        }

        return await buildsQueryService.GetAsync(
            championId,
            patch,
            position,
            ct,
            new ChampionBuildsScope(account.Id, account.PlatformId, MinPlayerGames));
    }
}
