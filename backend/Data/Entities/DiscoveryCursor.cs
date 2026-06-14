namespace Data.Entities;

/// <summary>
/// Per-platform sliding window cursor for ladder discovery (#486). The league
/// endpoints return the full Master/GM/Challenger list every run, so always
/// taking the top <c>MaxAccountsPerPlatformPerRun</c> re-processed the same top
/// accounts (a big reason <c>newAccounts</c> ≈ 0). This persists the offset to
/// resume from on the next run, advancing by the window and wrapping at the end
/// so successive runs sweep the whole ladder.
/// </summary>
public class DiscoveryCursor
{
    /// <summary>Platform the cursor tracks (e.g. "KR"); the primary key.</summary>
    public string PlatformId { get; set; } = string.Empty;

    /// <summary>Offset into the (distinct) ladder list to start the next run's window at.</summary>
    public int Offset { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
