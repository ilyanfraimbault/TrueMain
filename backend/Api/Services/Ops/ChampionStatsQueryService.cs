using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class ChampionStatsQueryService(TrueMainDbContext db) : IChampionStatsQueryService
{
    public async Task<IReadOnlyList<ChampionStatRow>> GetAsync(
        string? region,
        string? patch,
        string? position,
        int? queue,
        CancellationToken ct)
    {
        var normalizedRegion = Trimmed(region);
        var normalizedPatch = Trimmed(patch);
        var normalizedPosition = Trimmed(position);

        // Two independent per-champion aggregations folded together with a FULL
        // OUTER JOIN so a champion that appears in only one source (e.g. has
        // games but no mains yet, or vice-versa) still yields a row. The games
        // side honours region/patch/position/queue (joining matches for the
        // match-scoped filters); the mains side honours region only — patch,
        // position and queue have no meaning for main_champion_stats, which is
        // computed per account-champion rather than per match.
        //
        // Patch is matched against the normalised "MAJOR.MINOR" form of the raw
        // GameVersion via split_part, mirroring Core PatchVersion.Normalize.
        // Each nullable filter is guarded by an "{param}::type IS NULL OR ..."
        // clause so a null parameter means "no filter".
        FormattableString sql = $"""
            WITH games AS (
                SELECT p."ChampionId" AS "ChampionId", COUNT(*) AS "Games"
                FROM match_participants p
                INNER JOIN matches m ON m."Id" = p."MatchId"
                WHERE ({normalizedRegion}::text IS NULL OR m."PlatformId" = {normalizedRegion})
                  AND ({queue}::int IS NULL OR m."QueueId" = {queue})
                  AND ({normalizedPosition}::text IS NULL OR p."TeamPosition" = {normalizedPosition})
                  AND ({normalizedPatch}::text IS NULL
                       OR split_part(m."GameVersion", '.', 1) || '.' || split_part(m."GameVersion", '.', 2) = {normalizedPatch})
                GROUP BY p."ChampionId"
            ),
            mains AS (
                SELECT
                    s."ChampionId" AS "ChampionId",
                    COUNT(*) FILTER (WHERE s."IsMain") AS "Mains",
                    COUNT(*) FILTER (WHERE s."IsOtp") AS "Otps",
                    COUNT(*) FILTER (WHERE s."IsExtendedSample") AS "ExtendedSamples"
                FROM main_champion_stats s
                WHERE ({normalizedRegion}::text IS NULL OR s."PlatformId" = {normalizedRegion})
                GROUP BY s."ChampionId"
            )
            SELECT
                COALESCE(g."ChampionId", mn."ChampionId") AS "ChampionId",
                COALESCE(g."Games", 0)::bigint AS "Games",
                COALESCE(mn."Mains", 0)::int AS "Mains",
                COALESCE(mn."Otps", 0)::int AS "Otps",
                COALESCE(mn."ExtendedSamples", 0)::int AS "ExtendedSamples"
            FROM games g
            FULL OUTER JOIN mains mn ON mn."ChampionId" = g."ChampionId"
            ORDER BY "Games" DESC, "ChampionId"
            """;

        var rows = await db.Database.SqlQuery<ChampionStatRowResult>(sql).ToListAsync(ct);

        return rows
            .Select(row => new ChampionStatRow
            {
                ChampionId = row.ChampionId,
                Games = row.Games,
                Mains = row.Mains,
                Otps = row.Otps,
                ExtendedSamples = row.ExtendedSamples
            })
            .ToList();
    }

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ChampionStatRowResult(
        int ChampionId,
        long Games,
        int Mains,
        int Otps,
        int ExtendedSamples);
}
