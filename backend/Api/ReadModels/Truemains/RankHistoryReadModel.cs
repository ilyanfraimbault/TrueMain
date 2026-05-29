namespace TrueMain.ReadModels.Truemains;

/// <summary>
/// Rank-history payload behind <c>GET /truemains/{nameTag}/rank-history</c>.
/// Wraps the per-snapshot rows so the response shape can grow (totals,
/// pagination, queue scoping) without breaking the existing consumer.
/// </summary>
public sealed class RankHistoryReadModel
{
    public IReadOnlyList<RankHistoryEntryReadModel> Entries { get; init; }
        = Array.Empty<RankHistoryEntryReadModel>();
}

/// <summary>
/// One <c>RankSnapshot</c> row projected for the chart consumer. Snapshots
/// are append-on-change (see <c>RankSnapshotWriter</c>), so consecutive
/// entries are never duplicates of each other and the gap between two
/// rows is the period during which the player held the earlier rank.
/// </summary>
public sealed class RankHistoryEntryReadModel
{
    public DateTime CapturedAtUtc { get; init; }

    public string Tier { get; init; } = string.Empty;

    public string Division { get; init; } = string.Empty;

    public int LeaguePoints { get; init; }
}
