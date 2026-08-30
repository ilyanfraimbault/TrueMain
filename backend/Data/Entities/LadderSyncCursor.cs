namespace Data.Entities;

/// <summary>
/// Per-platform position of the paginated ladder sweep (#1312).
/// <para>
/// The apex tiers each come back whole in a single league-v4 call, but every tier below
/// Master is only reachable through <c>entries/{queue}/{tier}/{division}?page=N</c>, which
/// is far too large to walk in one run: Emerald alone is ~1 100 pages per platform. The
/// sweep therefore spends a bounded request budget each run and stores where it stopped,
/// so successive runs continue the descending walk instead of restarting at the top — the
/// same problem, and the same shape of answer, as the discovery sliding window (#486).
/// </para>
/// </summary>
public class LadderSyncCursor
{
    /// <summary>Platform the cursor tracks (e.g. "KR"); the primary key.</summary>
    public string PlatformId { get; set; } = string.Empty;

    /// <summary>Riot tier the next page belongs to (e.g. "DIAMOND").</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Roman division within <see cref="Tier"/> (e.g. "II").</summary>
    public string Division { get; set; } = string.Empty;

    /// <summary>1-based page to fetch next for that (tier, division) slot.</summary>
    public int Page { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
