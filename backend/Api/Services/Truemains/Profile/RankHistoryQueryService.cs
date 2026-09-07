using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.Profile;

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

public sealed class RankHistoryQueryService(
    TrueMainDbContext db,
    TruemainAccountResolver resolver) : IRankHistoryQueryService
{
    // Hard ceiling on the requested window. A snapshot is a single row
    // (~30 bytes), so even two years of dense ranked play comfortably
    // fits in one response — but the cap stops a hostile caller from
    // tipping the query into a sequential scan of the whole table.
    private const int MaxDays = 730;
    private const int DefaultDays = 90;

    public async Task<RankHistoryReadModel?> GetAsync(string nameTag, int days, CancellationToken ct)
    {
        var account = await resolver.ResolveAsync(nameTag, ct);
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
