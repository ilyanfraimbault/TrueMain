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
/// Roam metric for a champion at a position: the average number of out-of-lane
/// kill participations (kills + assists) per game, taken cumulatively at the
/// 5/10/15-minute marks. Pulls the bounded kill-position rows for the champion
/// (joined to its lane + the queue/patch/tracked-account population the sibling
/// reads share), classifies each against the lane and team side via
/// <see cref="LolMap.IsRoam"/>, and divides by the games actually played in that
/// lane (so games with no early roam still pull the average down). JUNGLE is
/// excluded — a jungler has no own lane, so every gank would read as a roam.
/// Computed live, cached 60s.
/// </summary>
public sealed class ChampionRoamQueryService(
    TrueMainDbContext db,
    IOptions<MainAnalysisOptions> options,
    IOptions<ChampionsListOptions> championsOptions,
    IMemoryCache cache)
    : IChampionRoamQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    // Cumulative early-game marks the roam average is reported at. The stored
    // kill positions are already bounded to before 15 min by the ingestor.
    private const int Window5Ms = 5 * 60 * 1000;
    private const int Window10Ms = 10 * 60 * 1000;
    private const int Window15Ms = 15 * 60 * 1000;

    // Riot team id for the blue side (bottom-left of the map); 200 is red.
    private const int BlueTeamId = 100;

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
        var bands = EloBracket.ResolveFilter(eloBracket);
        var bracketToken = bands is null ? "all" : EloBracket.Normalize(eloBracket)!;

        var cacheKey = $"champions:roam:{championId}:{position}:{normalizedPatch ?? "all"}:{bracketToken}";
        if (cache.TryGetValue<ChampionRoamResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        // Junglers have no own lane to roam from; the metric is meaningless for
        // them. Return an empty slice rather than a misleading near-100% roam.
        var ownLane = ExpectedZone(position);
        if (ownLane is MapZone.Jungle or MapZone.Unknown)
        {
            return Cache(cacheKey, Empty(championId, position, normalizedPatch));
        }

        var queueId = (int)options.Value.QueueId;
        var patchPrefix = normalizedPatch is null ? null : $"{normalizedPatch}.%";
        var minGames = championsOptions.Value.MinMatchupGames;

        // Denominator: games the champion played in this lane that also have
        // timeline coverage (i.e. produced kill-position rows). Games without a
        // timeline are excluded so they don't dilute the average toward zero.
        // Narrowed to the requested elo bands (null = every band), same as the
        // kill-position query below, so numerator and denominator agree.
        var gamesQuery = db.MatchParticipants.AsNoTracking()
            .Where(p => p.ChampionId == championId
                && p.TeamPosition == position
                && p.RiotAccountId != null
                && db.Matches.Any(m =>
                    m.Id == p.MatchId
                    && m.QueueId == queueId
                    && (normalizedPatch == null || EF.Functions.Like(m.GameVersion, patchPrefix!)))
                && db.MatchParticipantKillPositions.Any(k => k.MatchId == p.MatchId));
        if (bands is not null)
        {
            gamesQuery = gamesQuery.Where(p => bands.Contains(p.EloBracket));
        }

        var gamesPlayed = await gamesQuery
            .Select(p => p.MatchId)
            .Distinct()
            .CountAsync(ct);

        if (gamesPlayed < minGames)
        {
            return Cache(cacheKey, Empty(championId, position, normalizedPatch, gamesPlayed));
        }

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
            select new { killPosition.X, killPosition.Y, killPosition.TimestampMs, participant.TeamId, participant.EloBracket };

        // Narrow the champion side to the requested elo bands (null = every band).
        if (bands is not null)
        {
            killRows = killRows.Where(r => bands.Contains(r.EloBracket));
        }

        var rows = await killRows
            .Select(r => new { r.X, r.Y, r.TimestampMs, r.TeamId })
            .ToListAsync(ct);

        int roam5 = 0, roam10 = 0, roam15 = 0;
        foreach (var row in rows)
        {
            if (!LolMap.IsRoam(row.X, row.Y, ownLane, row.TeamId == BlueTeamId))
            {
                continue;
            }

            if (row.TimestampMs < Window5Ms)
            {
                roam5++;
            }

            if (row.TimestampMs < Window10Ms)
            {
                roam10++;
            }

            if (row.TimestampMs < Window15Ms)
            {
                roam15++;
            }
        }

        var response = new ChampionRoamResponse
        {
            ChampionId = championId,
            Position = position,
            Patch = normalizedPatch,
            Games = gamesPlayed,
            RoamKp5 = (double)roam5 / gamesPlayed,
            RoamKp10 = (double)roam10 / gamesPlayed,
            RoamKp15 = (double)roam15 / gamesPlayed
        };

        return Cache(cacheKey, response);
    }

    private ChampionRoamResponse Cache(string cacheKey, ChampionRoamResponse response)
    {
        cache.Set(cacheKey, response, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1
        });

        return response;
    }

    private static ChampionRoamResponse Empty(int championId, string position, string? patch, int games = 0)
        => new()
        {
            ChampionId = championId,
            Position = position,
            Patch = patch,
            Games = games,
            RoamKp5 = null,
            RoamKp10 = null,
            RoamKp15 = null
        };

    private static MapZone ExpectedZone(string position) => position switch
    {
        "TOP" => MapZone.TopLane,
        "MIDDLE" => MapZone.MidLane,
        "BOTTOM" => MapZone.BotLane,
        "UTILITY" => MapZone.BotLane,
        _ => MapZone.Unknown // JUNGLE and anything unrecognised: no own lane.
    };
}
