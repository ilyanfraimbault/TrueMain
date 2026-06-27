using Core.Lol.Patches;
using Core.Options;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Champions;

namespace TrueMain.Services.Champions;

/// <summary>
/// Average lead vs the lane opponent at each minute mark (5/10/15/20/30) for a
/// champion at a position. Joins per-interval timeline snapshots for the champion
/// side to the opposing lane's snapshot (same match + interval, same
/// <c>TeamPosition</c>, opposite <c>TeamId</c>) and averages the diffs. Same
/// queue / patch / tracked-account population as the sibling champion reads; no
/// aggregation table — computed from the raw snapshot rows on every request.
/// </summary>
public sealed class ChampionTimelineLeadsQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
    IMemoryCache cache)
    : IChampionTimelineLeadsQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    // Snapshots are now sampled every minute (issue #567), but games ingested
    // before that change only have these canonical marks. Pin the read to them so
    // the curve is identical across cohorts and every interval draws on the full
    // game population (off-grid minutes would otherwise be backed only by newer
    // games and fall foul of the per-interval sample floor).
    private static readonly int[] LeadIntervalMinutes = [5, 10, 15, 20, 30];

    public async Task<ChampionTimelineLeadsResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        CancellationToken ct)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToMajorMinor() : null;

        var cacheKey = $"champions:timeline-leads:{championId}:{position}:{normalizedPatch ?? "all"}";
        if (cache.TryGetValue<ChampionTimelineLeadsResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var queueId = (int)options.Value.QueueId;
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";

        // Reuses the matchup sample floor deliberately: timeline leads are drawn
        // from the same champion-aggregate population. Extract a dedicated
        // MinTimelineGames only if the two ever need independent tuning.
        var minGames = championsOptions.Value.MinMatchupGames;

        // Champion side: this champion at this position, on the configured queue
        // and (optional) patch, scoped to tracked accounts — same population as
        // the build / matchup / summary reads.
        var championRows = db.MatchParticipants
            .AsNoTracking()
            .Where(p1 => p1.ChampionId == championId
                && p1.TeamPosition == position
                && p1.RiotAccountId != null)
            .Where(p1 => db.Matches.Any(m =>
                m.Id == p1.MatchId
                && m.QueueId == queueId
                && (normalizedPatch == null || EF.Functions.Like(m.GameVersion, patchPrefix!))));

        // Pair each champion row with its lane opponent, then join both sides'
        // snapshots on the same interval, average the per-interval diffs, and
        // drop intervals below the sample floor — all in one SQL round-trip.
        var rows = await championRows
            // Exactly one opponent per champion row: the population is scoped to
            // the ranked queue (QueueId above), where Riot assigns each
            // TeamPosition to exactly one player per team, so (same match, same
            // TeamPosition, opposite TeamId) resolves to a single row. No Take(1)
            // is needed to keep Games honest — adding one would only force a
            // LATERAL join for a case this queue can't produce.
            .SelectMany(
                p1 => db.MatchParticipants.Where(p2 =>
                    p2.MatchId == p1.MatchId
                    && p2.TeamPosition == p1.TeamPosition
                    && p2.TeamId != p1.TeamId),
                (p1, p2) => new { p1, p2 })
            .SelectMany(
                pair => db.MatchParticipantTimelineSnapshots.Where(s1 =>
                    s1.MatchId == pair.p1.MatchId
                    && s1.ParticipantId == pair.p1.ParticipantId
                    && LeadIntervalMinutes.Contains(s1.IntervalMinute)),
                (pair, s1) => new { pair.p2, s1 })
            .SelectMany(
                self => db.MatchParticipantTimelineSnapshots.Where(s2 =>
                    s2.MatchId == self.p2.MatchId
                    && s2.ParticipantId == self.p2.ParticipantId
                    // Redundant with the equality below (s1 is already pinned to
                    // LeadIntervalMinutes), but stating it as a sargable IN lets
                    // Postgres restrict the opponent-snapshot side to the five
                    // marks up front instead of scanning the whole table and
                    // filtering only after the join. With parallel query disabled
                    // (#589) the unpruned side was a single-threaded seq scan of a
                    // multi-GB table — the cause of the 300s timeouts (#594).
                    && LeadIntervalMinutes.Contains(s2.IntervalMinute)
                    && s2.IntervalMinute == self.s1.IntervalMinute),
                (self, s2) => new
                {
                    self.s1.IntervalMinute,
                    GoldDiff = self.s1.TotalGold - s2.TotalGold,
                    CsDiff = self.s1.MinionsKilled + self.s1.JungleMinionsKilled
                        - s2.MinionsKilled - s2.JungleMinionsKilled,
                    KillsDiff = self.s1.Kills - s2.Kills,
                    LevelDiff = self.s1.Level - s2.Level,
                    XpDiff = self.s1.Xp - s2.Xp,
                    DamageDiff = self.s1.DamageToChampions - s2.DamageToChampions
                })
            .GroupBy(diff => diff.IntervalMinute)
            .Select(g => new
            {
                IntervalMinute = g.Key,
                Games = g.Count(),
                GoldDiff = g.Average(d => (double)d.GoldDiff),
                CsDiff = g.Average(d => (double)d.CsDiff),
                KillsDiff = g.Average(d => (double)d.KillsDiff),
                LevelDiff = g.Average(d => (double)d.LevelDiff),
                XpDiff = g.Average(d => (double)d.XpDiff),
                DamageDiff = g.Average(d => (double)d.DamageDiff)
            })
            .Where(x => x.Games >= minGames)
            .ToListAsync(ct);

        var intervals = rows
            .OrderBy(x => x.IntervalMinute)
            .Select(x => new ChampionTimelineLeadEntry
            {
                IntervalMinute = x.IntervalMinute,
                Games = x.Games,
                GoldDiff = x.GoldDiff,
                CsDiff = x.CsDiff,
                KillsDiff = x.KillsDiff,
                LevelDiff = x.LevelDiff,
                XpDiff = x.XpDiff,
                DamageDiff = x.DamageDiff
            })
            .ToList();

        var response = new ChampionTimelineLeadsResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Intervals = intervals
        };

        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        return response;
    }
}
