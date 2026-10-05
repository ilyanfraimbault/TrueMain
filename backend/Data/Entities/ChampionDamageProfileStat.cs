using Data.BuildFacts;

namespace Data.Entities;

/// <summary>
/// How a champion's damage splits by type <em>per build archetype</em> (#1905): the
/// physical, magic and true damage to champions summed over the games where its final
/// inventory leaned on one <see cref="ItemArchetype"/>. One row per
/// <c>(champion, position, patch, archetype)</c>, additive like
/// <see cref="ChampionProfileStat"/> and folded in the same pass, under the same
/// <c>Match.ProfileAggregated</c> gate.
///
/// <para>
/// <b>A sibling table, not a fifth key column on the profile.</b> The profile blends a
/// Kai'Sa's AD and AP games, and that blend is the right draft-time expectation when the
/// enemy's build is unknown — it is what the item-context fold reads. Splitting the
/// profile itself would multiply every row that fold scans and change what
/// <c>ChampionProfileFacts</c> means; this table only answers "does this champion go
/// both ways", next to it.
/// </para>
///
/// <para>
/// <b>No backfill.</b> The table fills forward from the deploy that created it: the
/// matches already flagged <c>ProfileAggregated</c> are not revisited, because resetting
/// the flag would fold them into <c>champion_profile_stats</c> a second time. Every
/// share read from here is relative to this table's own games, so a partially covered
/// patch is a smaller sample, never a skewed one.
/// </para>
/// </summary>
public class ChampionDamageProfileStat
{
    public Guid Id { get; set; }

    public int ChampionId { get; set; }

    /// <summary>Canonical <c>TeamPosition</c> (TOP / JUNGLE / MIDDLE / BOTTOM / UTILITY).</summary>
    public string Position { get; set; } = string.Empty;

    /// <summary>Canonical major.minor patch (e.g. "16.4").</summary>
    public string Patch { get; set; } = string.Empty;

    /// <summary>
    /// The single archetype the final inventory leaned on (see
    /// <c>DamageBuildArchetypes.Dominant</c>), <see cref="ItemArchetype.None"/> when it held
    /// no classified completed item. Never a combination of flags.
    /// </summary>
    public ItemArchetype Archetype { get; set; }

    /// <summary>Participants folded into this archetype.</summary>
    public int Games { get; set; }

    public long PhysicalDamageToChampionsSum { get; set; }

    public long MagicDamageToChampionsSum { get; set; }

    public long TrueDamageToChampionsSum { get; set; }

    public DateTime AggregatedAtUtc { get; set; }
}
