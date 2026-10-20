using Core.Lol.Identifiers;
using Core.Lol.Ranking;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using TrueMain.ReadModels.Ladder;

namespace TrueMain.Services.Ladder;

public interface IApexCutoffsQueryService
{
    /// <summary>
    /// The platform's Grandmaster / Challenger cutoffs over the last <paramref name="days"/>
    /// days. An empty list is a valid answer: nothing was measured in the window.
    /// </summary>
    Task<ApexCutoffsReadModel> GetAsync(PlatformId platform, int days, CancellationToken ct);
}

public sealed class ApexCutoffsQueryService(TrueMainDbContext db, IMemoryCache cache) : IApexCutoffsQueryService
{
    // The same window bounds as the rank history the cutoffs are drawn against.
    private const int MaxDays = 730;
    private const int DefaultDays = 90;

    // The ladder sync measures every few hours and every profile view asks: one read per
    // platform and window per quarter hour is plenty.
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    public async Task<ApexCutoffsReadModel> GetAsync(PlatformId platform, int days, CancellationToken ct)
    {
        var clamped = Math.Clamp(days <= 0 ? DefaultDays : days, 1, MaxDays);
        var key = (nameof(ApexCutoffsQueryService), platform.Value, clamped);
        if (cache.TryGetValue(key, out ApexCutoffsReadModel? cached) && cached is not null)
        {
            return cached;
        }

        var fromDay = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-clamped));
        var rows = await db.ApexCutoffSnapshots
            .AsNoTracking()
            .Where(s => s.PlatformId == platform.Value && s.CapturedOnUtc >= fromDay)
            .Select(s => new { s.CapturedOnUtc, s.Tier, s.MinLeaguePoints })
            .ToListAsync(ct);

        var entries = rows
            .GroupBy(row => row.CapturedOnUtc)
            .OrderBy(day => day.Key)
            .Select(day => new ApexCutoffReadModel
            {
                DateUtc = day.Key,
                GrandmasterMinLp = day.FirstOrDefault(row => row.Tier == RankTier.Grandmaster)?.MinLeaguePoints,
                ChallengerMinLp = day.FirstOrDefault(row => row.Tier == RankTier.Challenger)?.MinLeaguePoints,
            })
            .ToList();

        return cache.Store(key, new ApexCutoffsReadModel { Entries = entries }, Ttl);
    }
}
