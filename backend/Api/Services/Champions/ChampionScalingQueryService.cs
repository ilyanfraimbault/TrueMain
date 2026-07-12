using Core.Lol.Patches;
using Core.Lol.Ranking;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Win rate bucketed by game duration for a champion at a position, plus a single
/// scaling index (win rate of the longest qualifying bucket minus the shortest).
/// Same queue / patch / tracked-account population as the sibling champion reads;
/// computed live from match participants — no timeline or aggregation table.
/// </summary>
public sealed class ChampionScalingQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
    IMemoryCache cache)
    : IChampionScalingQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private static readonly string[] BucketLabels = ["<20m", "20-25m", "25-30m", "30-35m", "35m+"];

    public async Task<ChampionScalingResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        CancellationToken ct)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToMajorMinor() : null;

        // Resolve the elo filter to its bands (null = ALL, no clause). The cache
        // key carries the bracket so each band caches separately.
        var bands = EloBracket.ResolveFilter(eloBracket);
        var bracketToken = EloBracket.ResolveToken(eloBracket);

        var cacheKey = $"champions:scaling:{championId}:{position}:{normalizedPatch ?? "all"}:{bracketToken}";
        if (cache.TryGetValue<ChampionScalingResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var queueId = (int)options.Value.QueueId;
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";

        // Shares the matchup sample floor: a scaling bucket needs enough games for
        // its win rate to mean anything, same threshold the sibling reads use.
        var minGames = championsOptions.Value.MinMatchupGames;

        // The champion side: tracked rows for this champion + lane, optionally
        // narrowed to the requested elo bands.
        var participants = db.MatchParticipants
            .AsNoTracking()
            .Where(p => p.ChampionId == championId
                && p.TeamPosition == position
                && p.RiotAccountId != null);
        if (bands is not null)
        {
            participants = participants.Where(p => bands.Contains(p.EloBracket));
        }

        // Bucket each game by duration (CASE in GROUP BY), count games and wins per
        // bucket, drop thin buckets — one SQL round-trip.
        var rows = await participants
            .Join(
                db.Matches.Where(m =>
                    m.QueueId == queueId
                    && (normalizedPatch == null || EF.Functions.Like(m.GameVersion, patchPrefix!))),
                participant => participant.MatchId,
                match => match.Id,
                (participant, match) => new { participant.Win, match.GameDurationSeconds })
            .GroupBy(x => x.GameDurationSeconds < 1200 ? 0
                : x.GameDurationSeconds < 1500 ? 1
                : x.GameDurationSeconds < 1800 ? 2
                : x.GameDurationSeconds < 2100 ? 3
                : 4)
            .Select(g => new { Bucket = g.Key, Games = g.Count(), Wins = g.Sum(x => x.Win ? 1 : 0) })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        var buckets = rows
            .OrderBy(x => x.Bucket)
            .Select(x => new ChampionScalingBucket
            {
                Bucket = x.Bucket,
                Label = BucketLabels[x.Bucket],
                Games = x.Games,
                WinRate = (double)x.Wins / x.Games
            })
            .ToList();

        // Needs both ends to be meaningful: the win-rate gap from the shortest to
        // the longest qualifying bucket. Positive = the champion scales late.
        double? scalingIndex = buckets.Count >= 2
            ? buckets[^1].WinRate - buckets[0].WinRate
            : null;

        var response = new ChampionScalingResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Buckets = buckets,
            ScalingIndex = scalingIndex
        };

        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        return response;
    }
}
