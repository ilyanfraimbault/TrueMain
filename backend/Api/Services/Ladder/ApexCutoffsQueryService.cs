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
            .Select(s => new CutoffRow(s.CapturedOnUtc, s.Tier, s.MinLeaguePoints, s.IsEstimated))
            .ToListAsync(ct);

        var grandmaster = Smoothed(rows.Where(row => row.Tier == RankTier.Grandmaster));
        var challenger = Smoothed(rows.Where(row => row.Tier == RankTier.Challenger));

        var entries = grandmaster.Keys
            .Union(challenger.Keys)
            .Order()
            .Select(day => new ApexCutoffReadModel
            {
                DateUtc = day,
                GrandmasterMinLp = grandmaster.TryGetValue(day, out var gm) ? gm : null,
                ChallengerMinLp = challenger.TryGetValue(day, out var ch) ? ch : null,
            })
            .ToList();

        return cache.Store(key, new ApexCutoffsReadModel { Entries = entries }, Ttl);
    }

    /// <summary>
    /// One tier's cutoff per day. A measured day is served as read; an estimated one (#2181) is
    /// the median of itself and the neighbouring days on either side, so a single day whose
    /// tracked sample happened to miss the league's last players — or caught one intraday,
    /// before Riot's daily demotion — does not kink the line.
    /// </summary>
    internal static Dictionary<DateOnly, int> Smoothed(IEnumerable<CutoffRow> rows)
    {
        var byDay = rows.ToDictionary(row => row.Day);
        return byDay.Values.ToDictionary(
            row => row.Day,
            row =>
            {
                if (!row.IsEstimated)
                {
                    return row.MinLeaguePoints;
                }

                var window = new[] { row.Day.AddDays(-1), row.Day, row.Day.AddDays(1) }
                    .Where(byDay.ContainsKey)
                    .Select(day => byDay[day].MinLeaguePoints)
                    .Order()
                    .ToList();
                // The lower middle at the window's edge: an estimate errs high, never low.
                return window[(window.Count - 1) / 2];
            });
    }

    internal sealed record CutoffRow(DateOnly Day, RankTier Tier, int MinLeaguePoints, bool IsEstimated);
}
