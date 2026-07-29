namespace Data.Entities;

/// <summary>
/// The denominator every <see cref="ChampionBanStat"/> is divided by (#920): how
/// many matches were folded into a given patch / elo band. Stored rather than
/// counted at read time because matches are retired after a couple of patches
/// while their aggregates are kept forever (#466) — once a patch's matches are
/// gone, the only surviving record of how many there were is this row.
///
/// <para>
/// A match contributes one to every band that any of its tracked participants sat
/// in, plus one to the synthetic <c>ALL</c> band. So for band B the rate reads
/// "share of the matches involving a band-B player that banned this champion".
/// Numerator and denominator are folded from the same pass over the same matches,
/// so a match counted in two bands is counted in two bands on both sides and the
/// ratio stays honest. A filter spanning several bands (e.g. <c>GOLD_PLUS</c>) sums
/// both sides, which weights a match by how many of the selected bands it touched —
/// an approximation, and the reason <c>ALL</c> is stored exactly rather than summed.
/// </para>
/// </summary>
public class BanScopeTotal
{
    public Guid Id { get; set; }

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>Elo band, or the synthetic <c>ALL</c>. See <see cref="ChampionBanStat.EloBracket"/>.</summary>
    public string EloBracket { get; set; } = string.Empty;

    /// <summary>Matches folded into this scope.</summary>
    public int Matches { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
