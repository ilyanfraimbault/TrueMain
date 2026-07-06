using Core.Lol.Map;
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
/// Roam metric for a champion at a position: the share of its early-game kill
/// participations that happened outside its own lane. Pulls the bounded
/// kill-position rows for the champion (joined to its lane + the queue/patch/
/// tracked-account population the sibling reads share) and classifies each
/// against the lane via <see cref="LolMap"/>. Computed live, cached 60s.
/// </summary>
public sealed class ChampionRoamQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
    IMemoryCache cache)
    : IChampionRoamQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<ChampionRoamResponse> GetAsync(
        int championId,
        string position,
        string? patch,
        string? eloBracket,
        CancellationToken ct)
    {
        var normalizedPatch = string.IsNullOrWhiteSpace(patch)
            ? null
            : PatchVersion.TryParse(patch, out var parsed) ? parsed.ToMajorMinor() : null;

        // Resolve the elo filter to its bands (null = ALL, no clause); the cache
        // key carries the bracket so each band caches separately.
        var bands = EloBracket.ResolveBands(eloBracket);
        var bracketToken = bands is null ? "all" : EloBracket.Normalize(eloBracket)!;

        var cacheKey = $"champions:roam:{championId}:{position}:{normalizedPatch ?? "all"}:{bracketToken}";
        if (cache.TryGetValue<ChampionRoamResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var queueId = (int)options.Value.QueueId;
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";
        var minGames = championsOptions.Value.MinMatchupGames;

        var killRows =
            from killPosition in db.MatchParticipantKillPositions.AsNoTracking()
            join participant in db.MatchParticipants
                on new { killPosition.MatchId, killPosition.ParticipantId }
                equals new { participant.MatchId, participant.ParticipantId }
            where participant.ChampionId == championId
                && participant.TeamPosition == position
                && participant.RiotAccountId != null
                && db.Matches.Any(m =>
                    m.Id == killPosition.MatchId
                    && m.QueueId == queueId
                    && (normalizedPatch == null || EF.Functions.Like(m.GameVersion, patchPrefix!)))
            select new { killPosition.MatchId, killPosition.X, killPosition.Y, participant.EloBracket };

        // Narrow the champion side to the requested elo bands (null = every band).
        if (bands is not null)
        {
            killRows = killRows.Where(r => bands.Contains(r.EloBracket));
        }

        var rows = await killRows
            .Select(r => new { r.MatchId, r.X, r.Y })
            .ToListAsync(ct);

        var games = rows.Select(row => row.MatchId).Distinct().Count();
        var total = rows.Count;

        int outOfLane = 0;
        double? share = null;
        if (games >= minGames && total > 0)
        {
            var ownLane = ExpectedZone(position);
            outOfLane = rows.Count(row => LolMap.Classify(row.X, row.Y) != ownLane);
            share = (double)outOfLane / total;
        }

        var response = new ChampionRoamResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Games = games,
            KillParticipations = total,
            OutOfLaneParticipations = outOfLane,
            OutOfLaneShare = share
        };

        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        return response;
    }

    private static MapZone ExpectedZone(string position) => position switch
    {
        "TOP" => MapZone.TopLane,
        "MIDDLE" => MapZone.MidLane,
        "BOTTOM" => MapZone.BotLane,
        "UTILITY" => MapZone.BotLane,
        "JUNGLE" => MapZone.Jungle,
        _ => MapZone.Unknown
    };
}
