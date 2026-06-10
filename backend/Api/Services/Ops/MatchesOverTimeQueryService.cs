using System.Runtime.CompilerServices;
using Data;
using Microsoft.EntityFrameworkCore;
using TrueMain.ReadModels.Ops;

namespace TrueMain.Services.Ops;

public sealed class MatchesOverTimeQueryService(TrueMainDbContext db) : IMatchesOverTimeQueryService
{
    public async Task<IReadOnlyList<MatchTimeBucket>> GetAsync(
        MatchTimeGranularity granularity,
        string? region,
        CancellationToken ct)
    {
        var normalizedRegion = Trimmed(region);

        // Patch buckets are categorical (normalised "MAJOR.MINOR"), so they take a
        // different shape and, crucially, a different ordering: chronological by the
        // first game seen on each patch rather than lexical — otherwise "16.10"
        // would sort before "16.4". The time granularities all share one query,
        // parameterised only by the (validated) date_trunc unit.
        FormattableString sql = granularity == MatchTimeGranularity.Patch
            ? BuildPatchSql(normalizedRegion)
            : BuildPeriodSql(TruncUnit(granularity), normalizedRegion);

        var rows = await db.Database.SqlQuery<MatchTimeBucketResult>(sql).ToListAsync(ct);

        return rows
            .Select(row => new MatchTimeBucket { Bucket = row.Bucket, Matches = row.Matches })
            .ToList();
    }

    // week/month/year: truncate the game timestamp to the period start and emit it
    // as an unambiguous ISO-8601 UTC string. GameStartTimeUtc is a timestamptz, so
    // we pin truncation to UTC ("AT TIME ZONE 'UTC'" yields the UTC wall-clock
    // timestamp) before truncating — this makes the bucket boundaries independent of
    // the database session's TimeZone setting. to_char then renders the period start
    // as YYYY-MM-DDTHH:MM:SSZ.
    //
    // EF's SqlQuery turns every interpolation hole into a parameter, so the
    // date_trunc unit (which must be raw SQL, not a bound value) cannot ride as a
    // {hole}. We bake the validated unit literal into the format text and hand the
    // region to FormattableStringFactory.Create as the sole real parameter ({0}).
    private static FormattableString BuildPeriodSql(string truncUnit, string? region)
    {
        // truncUnit is a constant chosen by TruncUnit() from the validated enum — never
        // a user value — so inlining it as a literal fragment is safe. The format text
        // is a plain (non-interpolated) raw string so its {0} stays a literal
        // composite-format placeholder; we splice the trusted period fragment in via a
        // token replace, then hand region to Create as the sole {0} parameter.
        var period = $"date_trunc('{truncUnit}', \"GameStartTimeUtc\" AT TIME ZONE 'UTC')";

        var format =
            """
            SELECT
                to_char(@@PERIOD@@, 'YYYY-MM-DD"T"HH24:MI:SS"Z"') AS "Bucket",
                COUNT(*)::bigint AS "Matches"
            FROM matches
            WHERE ({0}::text IS NULL OR "PlatformId" = {0})
            GROUP BY @@PERIOD@@
            ORDER BY @@PERIOD@@
            """.Replace("@@PERIOD@@", period);

        return FormattableStringFactory.Create(format, region);
    }

    // patch: normalise GameVersion to "MAJOR.MINOR" (mirrors Core PatchVersion.Normalize
    // and ChampionStatsQueryService) and order patches chronologically by the earliest
    // game on each, so 16.10 follows 16.4 instead of sorting lexically before it.
    private static FormattableString BuildPatchSql(string? region)
        => $"""
            SELECT
                split_part("GameVersion", '.', 1) || '.' || split_part("GameVersion", '.', 2) AS "Bucket",
                COUNT(*)::bigint AS "Matches"
            FROM matches
            WHERE ({region}::text IS NULL OR "PlatformId" = {region})
            GROUP BY split_part("GameVersion", '.', 1) || '.' || split_part("GameVersion", '.', 2)
            ORDER BY min("GameStartTimeUtc")
            """;

    // Map the validated granularity to the literal date_trunc field. Switching here
    // (rather than interpolating the enum/string) guarantees only these three
    // constants ever reach the SQL — never a user-controlled value.
    private static string TruncUnit(MatchTimeGranularity granularity) => granularity switch
    {
        MatchTimeGranularity.Week => "week",
        MatchTimeGranularity.Month => "month",
        MatchTimeGranularity.Year => "year",
        _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Not a period granularity.")
    };

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record MatchTimeBucketResult(string Bucket, long Matches);
}
