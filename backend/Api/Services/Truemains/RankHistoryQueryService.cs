using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;

namespace TrueMain.Services.Truemains;

public sealed class RankHistoryQueryService(TrueMainDbContext db) : IRankHistoryQueryService
{
    // Hard ceiling on the requested window. A snapshot is a single row
    // (~30 bytes), so even two years of dense ranked play comfortably
    // fits in one response — but the cap stops a hostile caller from
    // tipping the query into a sequential scan of the whole table.
    private const int MaxDays = 730;
    private const int DefaultDays = 90;

    public async Task<RankHistoryReadModel?> GetAsync(string nameTag, int days, CancellationToken ct)
    {
        if (!NameTagParser.TryParse(nameTag, out var parsed))
        {
            return null;
        }

        var account = await db.RiotAccounts
            .AsNoTracking()
            .Where(a => a.GameName == parsed.GameName && a.TagLine == parsed.TagLine)
            .OrderByDescending(a => a.LastMatchIngestAtUtc ?? a.UpdatedAtUtc)
            .Select(a => new { a.Id })
            .FirstOrDefaultAsync(ct);

        if (account is null)
        {
            return null;
        }

        var clamped = Math.Clamp(days <= 0 ? DefaultDays : days, 1, MaxDays);
        var fromUtc = DateTime.UtcNow.AddDays(-clamped);

        var entries = await db.RankSnapshots
            .AsNoTracking()
            .Where(s => s.RiotAccountId == account.Id && s.CapturedAtUtc >= fromUtc)
            .OrderBy(s => s.CapturedAtUtc)
            .Select(s => new RankHistoryEntryReadModel
            {
                CapturedAtUtc = s.CapturedAtUtc,
                Tier = s.Tier,
                Division = s.Division,
                LeaguePoints = s.LeaguePoints,
            })
            .ToListAsync(ct);

        return new RankHistoryReadModel { Entries = entries };
    }
}
