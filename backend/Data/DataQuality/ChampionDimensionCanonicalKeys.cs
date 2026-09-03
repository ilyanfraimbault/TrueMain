namespace Data.DataQuality;

/// <summary>
/// What makes two <c>champion_dim_*</c> rows the same thing, spelled as SQL — and, since
/// #1418, spelled <em>once</em>: the schema objects that make a duplicate impossible are
/// built from these same constants, so the constraint, the repair that no longer exists
/// and the audit cannot disagree.
///
/// <para>
/// A dimension row's UNIQUE index is over the columns <em>as stored</em>. That is a
/// complete guard only when the stored order is itself the identity. Where a dimension
/// holds an <b>order-insensitive</b> component, the index cannot see a permutation:
/// <c>champion_dim_rune_pages</c> kept the two secondary perks in the player's click
/// order, so one page existed as <c>(8451, 8444)</c> and <c>(8444, 8451)</c> and its
/// games were split across both rows — 20 370 pairs, 48% of the dimension (#911).
/// <c>champion_dim_starter_items</c> failed the same way one level up: its key was a
/// string the application built by joining the basket in <em>price</em> order, so a
/// re-priced starter — or an item whose metadata went missing, which prices it at 0 —
/// re-keyed a basket it had already stored, and 17 baskets sat split in production.
/// </para>
///
/// <para>
/// <b>The fix is the schema, not a repair pass.</b> Each audited dimension now carries a
/// UNIQUE index over its canonical expression, so a permutation is rejected at
/// <c>INSERT</c>; the two column-order dimensions additionally carry a CHECK, so a writer
/// that stops normalising fails loudly rather than quietly re-splitting the dimension;
/// and the starter basket's key is a <b>stored generated column</b>, so no writer
/// computes it at all. The detector below survives as a regression alarm — it should read
/// zero for ever, and a non-zero means someone removed a constraint.
/// </para>
///
/// <para>
/// <b>Deliberately not constrained beyond their column index:</b>
/// <c>champion_dim_builds</c> and <c>champion_dim_skill_orders</c>. In both, the order
/// <em>is</em> the datum — the build path and the levelling sequence — so two rows holding
/// the same items in a different order are two different things. See
/// <see cref="ExemptTables"/>.
/// </para>
/// </summary>
public static class ChampionDimensionCanonicalKeys
{
    /// <summary>
    /// The canonical-key expression list for <c>champion_dim_rune_pages</c>: the UNIQUE
    /// index is built over exactly this, and the audit groups by exactly this.
    /// <c>LEAST</c>/<c>GREATEST</c> on the secondary pair is what collapses the two
    /// permutations onto one key whatever order the row is stored in.
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
    /// duplicate on its own, which is why the UNIQUE index above does not catch it — but
    /// the reader's canonical lookup would miss it and mint a second row, re-creating the
    /// split (#911). Hence the CHECK, which makes the state unreachable.
    /// </summary>
    public const string RunePageNonCanonical = """
        "SecondaryPerk1Id" > "SecondaryPerk2Id"
        """;

    /// <summary>The CHECK that keeps a rune page's secondary perks sorted on disk.</summary>
    public const string RunePageCanonicalCheck = """
        "SecondaryPerk1Id" <= "SecondaryPerk2Id"
        """;

    public const string RunePageCanonicalCheckName = "CK_champion_dim_rune_pages_canonical_secondary_perks";

    /// <summary>Name of the UNIQUE index over <see cref="RunePageCanonicalKey"/>.</summary>
    public const string RunePageCanonicalIndexName = "IX_champion_dim_rune_pages_canonical";

    /// <summary>
    /// The canonical-key expression list for <c>champion_dim_spell_pairs</c>. Flash+Ignite
    /// and Ignite+Flash are one loadout: exactly the #911 shape, one dimension over.
    /// </summary>
    public const string SpellPairCanonicalKey = """
        LEAST("Spell1Id", "Spell2Id"), GREATEST("Spell1Id", "Spell2Id")
        """;

    /// <summary>A spell pair stored in the player's order rather than sorted.</summary>
    public const string SpellPairNonCanonical = """
        "Spell1Id" > "Spell2Id"
        """;

    /// <summary>The CHECK that keeps a spell pair sorted on disk.</summary>
    public const string SpellPairCanonicalCheck = """
        "Spell1Id" <= "Spell2Id"
        """;

    public const string SpellPairCanonicalCheckName = "CK_champion_dim_spell_pairs_canonical_order";

    /// <summary>Name of the UNIQUE index over <see cref="SpellPairCanonicalKey"/>.</summary>
    public const string SpellPairCanonicalIndexName = "IX_champion_dim_spell_pairs_canonical";

    /// <summary>
    /// The function behind <c>champion_dim_starter_items."CanonicalKey"</c>: the basket as
    /// a sorted multiset, joined — which is what the row actually identifies.
    ///
    /// <para>
    /// It has to be a function rather than an inline expression because a generated column
    /// may not contain a subquery or an aggregate, and sorting a JSONB array in Postgres 17
    /// needs both. <c>IMMUTABLE</c> is not a promise made lightly here — the result depends
    /// on nothing but the argument, which is precisely what the old price-ordered key could
    /// not claim, and why it drifted.
    /// </para>
    ///
    /// <para>
    /// <c>coalesce(..., '')</c> is load-bearing: an empty basket aggregates to NULL, and a
    /// UNIQUE index lets any number of NULLs through, so without it the one row every
    /// no-starter game points at could silently become many.
    /// </para>
    /// </summary>
    public const string StarterItemsCanonicalKeyFunction = """
        CREATE OR REPLACE FUNCTION champion_dim_starter_items_canonical_key(items jsonb)
        RETURNS text
        LANGUAGE sql
        IMMUTABLE
        PARALLEL SAFE
        STRICT
        AS $function$
            SELECT coalesce(string_agg(item, '-' ORDER BY item::int, item), '')
            FROM jsonb_array_elements_text(items) AS item
        $function$;
        """;

    public const string StarterItemsCanonicalKeyFunctionName = "champion_dim_starter_items_canonical_key";

    /// <summary>The generated column's expression, as EF and the migration both spell it.</summary>
    public const string StarterItemsCanonicalKeySql =
        """champion_dim_starter_items_canonical_key("StarterItems")""";

    /// <summary>
    /// Dimensions whose stored column order <em>is</em> their identity, with the reason.
    /// Surfaced to the panel so "not audited" reads as a decision rather than an oversight.
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
    /// Every dimension whose identity needs more than its raw columns. Each one is now
    /// guarded by a constraint built from the same expression the audit groups on, so
    /// these counts are a regression alarm rather than a work queue.
    /// </summary>
    public static IReadOnlyList<ChampionDimensionAudit> AuditedTables { get; } =
    [
        new(
            "champion_dim_rune_pages",
            "champion_dim_rune_pages",
            RunePageCanonicalKey,
            RunePageNonCanonical,
            "The two secondary perks are a set, not a sequence (#911). A UNIQUE index over "
            + "the sorted pair and a CHECK on the stored order make both states unreachable."),
        new(
            "champion_dim_spell_pairs",
            "champion_dim_spell_pairs",
            SpellPairCanonicalKey,
            SpellPairNonCanonical,
            "A summoner-spell pair is a set; a UNIQUE index over the sorted pair and a CHECK "
            + "on the stored order make both states unreachable."),
        new(
            "champion_dim_starter_items",
            "champion_dim_starter_items",
            """ "CanonicalKey" """,
            // No non-canonical predicate, and this time because the state cannot exist:
            // Postgres computes the key from the basket, so no stored order carries
            // identity and there is nothing for a row to be out of order about.
            NonCanonicalPredicate: null,
            "A starter basket is a set. Postgres generates its key from the basket itself, "
            + "so the application can no longer key the same basket two ways (#1418).")
    ];
}

/// <summary>
/// One dimension table the duplicate detector groups over.
/// </summary>
/// <param name="TableName">Postgres table name, used as the row label.</param>
/// <param name="FromSql">The <c>FROM</c> clause the grouping runs over.</param>
/// <param name="CanonicalKeyExpression">
/// The <c>GROUP BY</c> expression list that collapses equivalent rows onto one key — the
/// same expression the table's UNIQUE index is built over.
/// </param>
/// <param name="NonCanonicalPredicate">
/// A predicate matching rows stored outside canonical order. <see langword="null"/> where
/// the notion does not apply because no stored order carries identity, in which case the
/// count is reported as unknown rather than as zero.
/// </param>
/// <param name="Rationale">Why this table needs more than a plain column index.</param>
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
