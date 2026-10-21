namespace Data.Entities;

/// <summary>
/// The light per-patch record (#1956, decided in #1467): games and wins per
/// <c>(queue, patch, champion, position, elo bracket, population)</c>, the grain of the
/// "Trend by patch" chart and of its rank filter. It is
/// <c>champion_aggregate_scopes</c> summed over the account and the platform, so it
/// holds a few thousand rows per patch where the scopes hold one per account.
///
/// <para>
/// <b>Why it exists.</b> The full aggregates are to be kept for the current and the
/// previous patch only (#1958); this record is what remains of a patch afterwards, so
/// the trend can still draw five patches. Its pick-rate denominator is the sum of
/// <see cref="Games"/> over every champion of the same lane, bracket and population —
/// the record holds every champion, so it needs no stored total. The ban rate is not
/// here: bans have no position and their own stored denominator, and
/// <c>champion_ban_stats</c> / <c>ban_scope_totals</c> are already at the light grain,
/// so they belong to the light level and retention keeps them too.
/// </para>
///
/// <para>
/// <b>Kept in step with the scopes, never recomputed.</b> The pattern aggregation folds
/// the change of every slice it replaces into this table in the same transaction —
/// subtracting the slice's old scopes, adding its new ones — so the two always agree on
/// a patch whose scopes still exist. Once a patch's scopes are purged the record is its
/// only history, which is why retention never deletes it. A table found empty is
/// filled once from the scopes before the first fold (the backfill of every patch
/// whose full aggregates still exist).
/// </para>
/// </summary>
public class ChampionPatchStat
{
    public int QueueId { get; set; }

    /// <summary>The scope's <c>GameVersion</c>: the canonical major.minor patch.</summary>
    public string Patch { get; set; } = string.Empty;

    public int ChampionId { get; set; }

    /// <summary>The scope's position, as stored there (the blank "no lane" sentinel included).</summary>
    public string Position { get; set; } = string.Empty;

    /// <summary>
    /// A per-tier <c>EloBracket</c> value, never <c>ALL</c>: each game sits in one
    /// bracket here, as on the scopes, so the unfiltered read is the sum of the rows.
    /// </summary>
    public string EloBracket { get; set; } = string.Empty;

    /// <summary>The scope's <c>IsMain</c>: the truemain population, or the widened one (#1346).</summary>
    public bool IsMain { get; set; }

    public int Games { get; set; }

    public int Wins { get; set; }

    /// <summary>When a fold last changed this row.</summary>
    public DateTime AggregatedAtUtc { get; set; }
}
