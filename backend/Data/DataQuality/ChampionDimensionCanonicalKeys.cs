namespace Data.DataQuality;

/// <summary>
/// What makes two <c>champion_dim_*</c> rows the same thing, spelled as SQL — the
/// generalisation of #911.
///
/// <para>
/// A dimension row's UNIQUE index is over the columns <em>as stored</em>. That is a
/// complete guard only when the stored column order is itself the identity. Where a
/// dimension holds an <b>order-insensitive</b> component, the index cannot see a
/// permutation: <c>champion_dim_rune_pages</c> kept the two secondary perks in the
/// player's click order, so one page existed as <c>(8451, 8444)</c> and
/// <c>(8444, 8451)</c> and its games were split across both rows — 20 370 pairs, 48%
/// of the dimension, before anyone noticed (#911).
/// </para>
///
/// <para>
/// Both halves of the fix read from here, deliberately: the Ingestor's
/// <c>RunePageDeduplicationProcess</c> merges rows on the canonical key, and the admin
/// data-quality detector counts rows that still share one. Two copies of these
/// expressions would eventually disagree, and a detector that disagrees with the
/// repair it is auditing reports a clean bill of health for a live bug.
/// </para>
///
/// <para>
/// <b>Deliberately not audited:</b> <c>champion_dim_builds</c> and
/// <c>champion_dim_skill_orders</c>. In both, the order <em>is</em> the datum — the
/// build path and the skill sequence — so two rows holding the same items or skills in
/// a different order are two different things, and the UNIQUE index over the stored
/// columns is a complete guard. Adding them here would cost a full scan per page load
/// for a structurally guaranteed zero. See <see cref="ExemptTables"/>.
/// </para>
/// </summary>
public static class ChampionDimensionCanonicalKeys
{
    /// <summary>
    /// The canonical-key partition for <c>champion_dim_rune_pages</c>.
    /// <c>LEAST</c>/<c>GREATEST</c> on the secondary pair is what collapses the two
    /// permutations onto one key without needing the pair sorted on disk yet.
    /// </summary>
    public const string RunePageCanonicalKey = """
        "PrimaryStyleId", "PrimaryKeystoneId", "PrimaryPerk1Id",
        "PrimaryPerk2Id", "PrimaryPerk3Id", "SecondaryStyleId",
        LEAST("SecondaryPerk1Id", "SecondaryPerk2Id"),
        GREATEST("SecondaryPerk1Id", "SecondaryPerk2Id"),
        "StatOffense", "StatFlex", "StatDefense"
        """;

    /// <summary>
    /// A rune page still holding its secondary perks in the player's order. Not a
    /// duplicate on its own — but the reader's canonical lookup would miss it and mint
    /// a second row, re-creating the split (#911), so it is the leading indicator.
    /// </summary>
    public const string RunePageNonCanonical = """
        "SecondaryPerk1Id" > "SecondaryPerk2Id"
        """;

    /// <summary>
    /// The canonical-key partition for <c>champion_dim_spell_pairs</c>. Flash+Ignite and
    /// Ignite+Flash are one loadout, and the writer already stores the pair sorted
    /// (<c>SummonerSpellPair.Canonical()</c>) — but nothing in the database enforces
    /// that, and the two-column UNIQUE index treats the swap as a distinct row. Exactly
    /// the #911 shape, one dimension over.
    /// </summary>
    public const string SpellPairCanonicalKey = """
        LEAST("Spell1Id", "Spell2Id"), GREATEST("Spell1Id", "Spell2Id")
        """;

    /// <summary>A spell pair stored in the player's order rather than sorted.</summary>
    public const string SpellPairNonCanonical = """
        "Spell1Id" > "Spell2Id"
        """;

    /// <summary>
    /// Source of the canonical key for <c>champion_dim_starter_items</c>: the basket as a
    /// sorted multiset, which is what the row actually identifies. Expressed as a lateral
    /// rather than a scalar subquery in the <c>GROUP BY</c> so the grouping expression is
    /// a plain column reference.
    ///
    /// <para>
    /// The UNIQUE index is on <c>StarterItemsKey</c>, a string built by joining the items
    /// in the analyser's canonical order — <em>most expensive first</em>. That order is
    /// derived from patch-dependent item prices, so the same basket can key differently
    /// once Riot re-prices a starter, splitting it into two rows. This is not
    /// hypothetical: the <c>CanonicalizeStarterItemsKeys</c> migration already merged one
    /// generation of split rows, and its own comment warns that an item missing from its
    /// hard-coded price table "will produce a different canonical order than the ingestor
    /// emits today, leaving fresh dim rows split from legacy ones". Grouping on the sorted
    /// multiset is price-independent and therefore catches both generations.
    /// </para>
    /// </summary>
    public const string StarterItemsFrom = """
        champion_dim_starter_items dim
        CROSS JOIN LATERAL (
            SELECT array_agg(item ORDER BY item) AS canonical_key
            FROM jsonb_array_elements_text(dim."StarterItems") AS item
        ) canonical
        """;

    /// <summary>The grouping expression produced by <see cref="StarterItemsFrom"/>.</summary>
    public const string StarterItemsCanonicalKey = "canonical.canonical_key";

    /// <summary>
    /// Dimensions whose stored column order <em>is</em> their identity, with the reason.
    /// Surfaced to the panel so "not audited" reads as a decision rather than an
    /// oversight.
    /// </summary>
    public static IReadOnlyList<ChampionDimensionExemption> ExemptTables { get; } =
    [
        new(
            "champion_dim_builds",
            "Item slots are the build path — the same six items bought in another order "
            + "is a different build, so the 8-column UNIQUE index is a complete guard."),
        new(
            "champion_dim_skill_orders",
            "The key is the levelling sequence itself; a permutation is a different skill "
            + "order, so the UNIQUE index on it is a complete guard.")
    ];

    /// <summary>
    /// Every dimension with an order-insensitive component, i.e. every one where a
    /// permutation can slip past the UNIQUE index.
    /// </summary>
    public static IReadOnlyList<ChampionDimensionAudit> AuditedTables { get; } =
    [
        new(
            "champion_dim_rune_pages",
            "champion_dim_rune_pages",
            RunePageCanonicalKey,
            RunePageNonCanonical,
            "The two secondary perks are a set, not a sequence (#911)."),
        new(
            "champion_dim_spell_pairs",
            "champion_dim_spell_pairs",
            SpellPairCanonicalKey,
            SpellPairNonCanonical,
            "A summoner-spell pair is a set; the writer sorts it, the schema does not."),
        new(
            "champion_dim_starter_items",
            StarterItemsFrom,
            StarterItemsCanonicalKey,
            // No non-canonical predicate: the canonical order is price-desc, and prices
            // are not in the database. Reporting "unchecked" (null) beats reproducing the
            // migration's hard-coded price table here, which is exactly the thing that
            // drifts. Set-equal duplicates are still caught, which is the actionable half.
            NonCanonicalPredicate: null,
            "A starter basket is a set; its key is ordered by patch-dependent prices.")
    ];
}

/// <summary>
/// One dimension table the duplicate detector groups over.
/// </summary>
/// <param name="TableName">Postgres table name, used as the row label.</param>
/// <param name="FromSql">
/// The <c>FROM</c> clause the grouping runs over — the bare table for most dimensions, a
/// lateral for the one whose key has to be derived from a JSONB array.
/// </param>
/// <param name="CanonicalKeyExpression">
/// The <c>GROUP BY</c> expression list that collapses equivalent rows onto one key.
/// </param>
/// <param name="NonCanonicalPredicate">
/// A predicate matching rows stored outside canonical order — the leading indicator that
/// duplicates are about to be minted again. <see langword="null"/> when the canonical
/// order cannot be expressed in SQL, in which case the count is reported as unknown
/// rather than as zero.
/// </param>
/// <param name="Rationale">Why this table needs more than its UNIQUE index.</param>
public sealed record ChampionDimensionAudit(
    string TableName,
    string FromSql,
    string CanonicalKeyExpression,
    string? NonCanonicalPredicate,
    string Rationale);

/// <summary>A dimension table deliberately left out of the duplicate audit, and why.</summary>
/// <param name="TableName">Postgres table name.</param>
/// <param name="Reason">Why its UNIQUE index is already a complete guard.</param>
public sealed record ChampionDimensionExemption(string TableName, string Reason);
