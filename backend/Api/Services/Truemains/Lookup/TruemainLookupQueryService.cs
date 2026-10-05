using Core.Options;
using Core.Truemains;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using TrueMain.Options;
using TrueMain.ReadModels.Truemains;
using TrueMain.Services.Truemains.Identity;

namespace TrueMain.Services.Truemains.Lookup;

public interface ITruemainLookupQueryService
{
    /// <summary>
    /// Which of the players of one game are true mains of the champion they are
    /// on (#1910). A player who is not, or whom TrueMain does not track, is
    /// absent from the answer.
    /// </summary>
    Task<TruemainLookupResponse> LookupAsync(TruemainLookupRequest request, CancellationToken ct);
}

/// <summary>
/// The desktop app's true-main mark on the loading screen and the Game page:
/// up to ten (Riot ID, champion) pairs answered in one SQL round trip.
/// </summary>
/// <remarks>
/// <para>
/// <b>Same population as the leaderboard.</b> A pair is a true main when its
/// account is ranked (<c>Score IS NOT NULL</c>), on an exposed platform, and has
/// an <c>IsMain AND IsActive</c> row on the champion past the leaderboard's
/// ranked-games floor — the predicate of <c>LeaderboardEligibility</c>, so no
/// one carries the mark who cannot be found on <c>/truemains</c>.
/// </para>
/// <para>
/// <b>Matched by Riot ID, on the game's platform.</b> Puuids are encrypted per
/// API key, so the one the client knows is not ours; the Riot ID is matched the
/// way <see cref="TruemainAccountResolver"/> does — lowered on both halves,
/// through <c>IX_riot_accounts_game_name_tag_line_lower</c>, the most recently
/// active row winning a collision — narrowed to the platform first, so an EUW
/// Riot ID never lands on a KR account.
/// </para>
/// <para>
/// Every app in the same game asks the same question as the game loads, so
/// the answer is cached for a few minutes and concurrent misses share a pass.
/// <c>main_champion_stats</c> only moves when main analysis reruns.
/// </para>
/// </remarks>
public sealed class TruemainLookupQueryService(
    TrueMainDbContext db,
    IOptions<TruemainsLeaderboardOptions> leaderboardOptions,
    IOptions<MainAnalysisOptions> mainAnalysisOptions,
    IMemoryCache cache,
    ILogger<TruemainLookupQueryService> logger) : ITruemainLookupQueryService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // Static because the service is scoped: coalescing is across requests.
    private static readonly RequestCoalescer<TruemainLookupResponse> Coalescer = new();

    private static readonly HashSet<string> ExposedPlatforms =
        new(RegionFilterParser.AllExposedPlatforms(), StringComparer.OrdinalIgnoreCase);

    private static readonly TruemainLookupResponse Empty = new();

    public async Task<TruemainLookupResponse> LookupAsync(TruemainLookupRequest request, CancellationToken ct)
    {
        // A platform the leaderboard does not show has no true mains to mark.
        if (request.Pairs.Count == 0 || !ExposedPlatforms.Contains(request.PlatformId))
        {
            return Empty;
        }

        var cacheKey = $"truemains:lookup:{request.Key}";
        if (cache.TryGetValue<TruemainLookupResponse>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        // The owner waits for the pass even if its caller leaves: the query
        // runs on its request-scoped context (see RequestCoalescer).
        return await Coalescer.GetOrJoinAsync(
            cacheKey,
            async () =>
            {
                var response = await QueryAsync(request, CancellationToken.None);
                logger.LogInformation(
                    "[truemain-lookup] platform={PlatformId} asked={Asked} found={Found}",
                    request.PlatformId, request.Pairs.Count, response.Players.Count);
                return cache.Store(cacheKey, response, CacheTtl);
            },
            ct,
            ownerAwaitsToCompletion: true);
    }

    private async Task<TruemainLookupResponse> QueryAsync(TruemainLookupRequest request, CancellationToken ct)
    {
        var platform = request.PlatformId;
        var names = request.Pairs.Select(p => p.GameNameLower).ToArray();
        var tags = request.Pairs.Select(p => p.TagLineLower).ToArray();
        var champions = request.Pairs.Select(p => p.ChampionId).ToArray();
        var minGames = Math.Max(0, leaderboardOptions.Value.MinRankedGames);

        // One account per asked pair (DISTINCT ON its ordinal, the resolver's
        // tiebreak), then eligibility on that account only: an older row of a
        // recycled Riot ID must not lend the current holder its mark.
        FormattableString sql = $"""
            WITH asked AS (
                SELECT *
                FROM unnest({names}::text[], {tags}::text[], {champions}::int[])
                     WITH ORDINALITY AS w("GameName", "TagLine", "ChampionId", "Ordinal")
            ),
            resolved AS (
                SELECT DISTINCT ON (w."Ordinal")
                    w."ChampionId" AS "ChampionId",
                    a."Puuid" AS "Puuid",
                    a."PlatformId" AS "PlatformId",
                    a."GameName" AS "GameName",
                    a."TagLine" AS "TagLine",
                    a."Score" AS "Score"
                FROM asked w
                JOIN riot_accounts a
                  ON lower(a."GameName") = w."GameName"
                 AND lower(a."TagLine") = w."TagLine"
                WHERE a."PlatformId" = {platform}
                ORDER BY w."Ordinal", COALESCE(a."LastMatchIngestAtUtc", a."UpdatedAtUtc") DESC, a."Id"
            )
            SELECT
                r."GameName" AS "GameName",
                r."TagLine" AS "TagLine",
                m."ChampionId" AS "ChampionId",
                m."ChampionMatches" AS "ChampionMatches",
                m."TotalMatches" AS "TotalMatches",
                m."PlayRate" AS "PlayRate",
                m."IsOtp" AS "IsOtp",
                m."MasteryPoints" AS "MasteryPoints",
                m."MasteryRank" AS "MasteryRank"
            FROM resolved r
            JOIN main_champion_stats m
              ON m."PlatformId" = r."PlatformId"
             AND m."Puuid" = r."Puuid"
             AND m."ChampionId" = r."ChampionId"
            WHERE r."Score" IS NOT NULL
              AND m."IsMain" = true
              AND m."IsActive" = true
              AND m."TotalMatches" >= {minGames}
            """;

        var rows = await db.Database.SqlQuery<Row>(sql).ToListAsync(ct);
        var floor = mainAnalysisOptions.Value.PlayRateFloor;

        return new TruemainLookupResponse
        {
            Players = rows
                .Select(row => new TruemainLookupEntryReadModel
                {
                    RiotId = $"{row.GameName}#{row.TagLine}",
                    NameTag = $"{row.GameName}-{row.TagLine}",
                    ChampionId = row.ChampionId,
                    ChampionMatches = row.ChampionMatches,
                    TotalMatches = row.TotalMatches,
                    PlayRate = row.PlayRate,
                    IsOtp = row.IsOtp,
                    MasteryPoints = row.MasteryPoints,
                    Dedication = DedicationScore.Compute(
                        new DedicationInputs(row.PlayRate, row.MasteryPoints, row.MasteryRank), floor).Score,
                })
                .OrderBy(entry => entry.RiotId, StringComparer.Ordinal)
                .ToList(),
        };
    }

    private sealed record Row(
        string GameName,
        string TagLine,
        int ChampionId,
        int ChampionMatches,
        int TotalMatches,
        double PlayRate,
        bool IsOtp,
        long? MasteryPoints,
        int? MasteryRank);
}
