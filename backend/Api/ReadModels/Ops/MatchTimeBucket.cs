namespace TrueMain.ReadModels.Ops;

/// <summary>
/// One time bucket of the "matches over time" histogram (<c>GET
/// /ops/stats/matches-over-time</c>). Buckets are ordered chronologically and
/// counted by <em>game date</em> (<c>Match.GameStartTimeUtc</c>), not ingestion
/// date — game date spreads the distribution meaningfully, whereas
/// <c>CreatedAtUtc</c> clusters on the few days the ingestor actually ran.
///
/// <para><see cref="Bucket"/> shape depends on the requested granularity:
/// for week/month/year it is the ISO-8601 timestamp of the truncated period
/// start (via <c>date_trunc</c>); for patch it is the normalised "MAJOR.MINOR"
/// form of <c>GameVersion</c> (e.g. <c>16.4</c>). The frontend formats the
/// per-granularity label.</para>
/// </summary>
public sealed record MatchTimeBucket
{
    public string Bucket { get; init; } = string.Empty;

    public long Matches { get; init; }
}
