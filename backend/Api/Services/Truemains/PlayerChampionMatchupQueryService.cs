using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Champions;
using TrueMain.Services.Champions;

namespace TrueMain.Services.Truemains;

public sealed class PlayerChampionMatchupQueryService(
    TrueMainDbContext db,
    IChampionMatchupQueryService matchupQueryService) : IPlayerChampionMatchupQueryService
{
    public async Task<ChampionMatchupsResponse?> GetAsync(
        string nameTag,
        int championId,
        string position,
        string? patch,
        int? opponentChampionId,
        CancellationToken ct)
    {
        if (!NameTagParser.TryParse(nameTag, out var parsed))
        {
            return null;
        }

        // Resolve the name tag the same way the profile + matches + builds
        // endpoints do (most-recently-active row on a cross-region collision)
        // so all routes agree on which account a name tag means.
        var accountId = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.GameName == parsed.GameName && a.TagLine == parsed.TagLine)
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);

        if (accountId is null)
        {
            return null;
        }

        return await matchupQueryService.GetAsync(
            championId,
            position,
            patch,
            accountId,
            opponentChampionId,
            ct);
    }
}
