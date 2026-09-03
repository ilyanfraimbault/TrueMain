using Data.DataQuality;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Moves each champion dimension's identity into the schema, so a duplicate cannot be
    /// stored rather than being repaired afterwards (#1418, closing the loop on #911 and
    /// the <c>CanonicalizeStarterItemsKeys</c> generation before it).
    ///
    /// <para>
    /// Three guards, one per audited dimension: a UNIQUE index over the canonical
    /// expression, so a permutation is rejected at <c>INSERT</c>; a CHECK on the two
    /// dimensions whose canonical form is a column order, so a writer that stops
    /// normalising fails on the spot instead of quietly splitting the dimension; and, for
    /// starter baskets, a stored generated column, so no writer computes the key at all.
    /// The old key was a string the application built by joining the basket in price
    /// order — identity that depended on Riot's price list, which is why 17 baskets sat
    /// split in production the day this was written.
    /// </para>
    ///
    /// <para>
    /// <b>The merge has to run first</b>, and it runs here rather than in a pipeline step
    /// because the index build fails while a duplicate is still in the table. Sized on
    /// production: 419 starter rows, 88 998 rune pages, 36 spell pairs and 412 500 pattern
    /// rows, of which ~100 k point at a split basket. That is seconds of transactional
    /// work, and none of it is a startup migration — preprod and prod apply the script out
    /// of band (<c>docs/production-migrations.md</c>), which is also why no statement here
    /// is <c>CONCURRENTLY</c>: it could not run inside the script's transaction.
    /// </para>
    ///
    /// <para>
    /// The merge is written for N rows per group even though production's are all pairs:
    /// the rune-page repair could assume pairs because the old 11-column index bounded a
    /// canonical group at two rows, and the starter dimension never had that bound.
    /// </para>
    /// </remarks>
    public partial class EnforceChampionDimensionCanonicalIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The generated column's expression. Created first: the column below depends
            // on it, and the merge already calls it to group the baskets.
            migrationBuilder.Sql(ChampionDimensionCanonicalKeys.StarterItemsCanonicalKeyFunction);

            MergeDuplicates(
                migrationBuilder,
                table: "champion_dim_starter_items",
                canonicalKey: ChampionDimensionCanonicalKeys.StarterItemsCanonicalKeySql,
                patternColumn: "StarterItemsId");

            MergeDuplicates(
                migrationBuilder,
                table: "champion_dim_rune_pages",
                canonicalKey: ChampionDimensionCanonicalKeys.RunePageCanonicalKey,
                patternColumn: "RunePageId");

            MergeDuplicates(
                migrationBuilder,
                table: "champion_dim_spell_pairs",
                canonicalKey: ChampionDimensionCanonicalKeys.SpellPairCanonicalKey,
                patternColumn: "SpellPairId");

            // Now that no two rows share a canonical key, putting the remaining ones in
            // canonical order cannot collide — and the CHECKs below would reject them.
            migrationBuilder.Sql(
                $"""
                UPDATE champion_dim_rune_pages
                SET "SecondaryPerk1Id" = "SecondaryPerk2Id",
                    "SecondaryPerk2Id" = "SecondaryPerk1Id"
                WHERE {ChampionDimensionCanonicalKeys.RunePageNonCanonical};
                """);

            migrationBuilder.Sql(
                $"""
                UPDATE champion_dim_spell_pairs
                SET "Spell1Id" = "Spell2Id",
                    "Spell2Id" = "Spell1Id"
                WHERE {ChampionDimensionCanonicalKeys.SpellPairNonCanonical};
                """);

            migrationBuilder.DropIndex(
                name: "IX_champion_dim_starter_items_StarterItemsKey",
                table: "champion_dim_starter_items");

            migrationBuilder.DropIndex(
                name: "IX_champion_dim_spell_pairs_Spell1Id_Spell2Id",
                table: "champion_dim_spell_pairs");

            migrationBuilder.DropIndex(
                name: "IX_champion_dim_rune_pages_PrimaryStyleId_PrimaryKeystoneId_Pr~",
                table: "champion_dim_rune_pages");

            migrationBuilder.DropColumn(
                name: "StarterItemsKey",
                table: "champion_dim_starter_items");

            migrationBuilder.AddColumn<string>(
                name: "CanonicalKey",
                table: "champion_dim_starter_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                computedColumnSql: ChampionDimensionCanonicalKeys.StarterItemsCanonicalKeySql,
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_dim_starter_items_CanonicalKey",
                table: "champion_dim_starter_items",
                column: "CanonicalKey",
                unique: true);

            // Expression indexes, which EF cannot model — hence the raw SQL and the
            // absence of these two from the model snapshot. They are the guard the
            // eleven-column and two-column indexes they replace could never be: those
            // were over the columns as stored, and a permutation is a different tuple.
            migrationBuilder.Sql(
                $"""
                CREATE UNIQUE INDEX "{ChampionDimensionCanonicalKeys.RunePageCanonicalIndexName}"
                ON champion_dim_rune_pages ({ChampionDimensionCanonicalKeys.RunePageCanonicalKey});
                """);

            migrationBuilder.Sql(
                $"""
                CREATE UNIQUE INDEX "{ChampionDimensionCanonicalKeys.SpellPairCanonicalIndexName}"
                ON champion_dim_spell_pairs ({ChampionDimensionCanonicalKeys.SpellPairCanonicalKey});
                """);

            migrationBuilder.CreateIndex(
                name: "IX_champion_dim_rune_pages_PrimaryKeystoneId",
                table: "champion_dim_rune_pages",
                column: "PrimaryKeystoneId");

            migrationBuilder.AddCheckConstraint(
                name: ChampionDimensionCanonicalKeys.RunePageCanonicalCheckName,
                table: "champion_dim_rune_pages",
                sql: ChampionDimensionCanonicalKeys.RunePageCanonicalCheck);

            migrationBuilder.AddCheckConstraint(
                name: ChampionDimensionCanonicalKeys.SpellPairCanonicalCheckName,
                table: "champion_dim_spell_pairs",
                sql: ChampionDimensionCanonicalKeys.SpellPairCanonicalCheck);
        }

        /// <inheritdoc />
        /// <remarks>
        /// The merge is not undone — the folded games belong to one row now, and splitting
        /// them back apart is not a thing a schema change can do. Nor is the old
        /// price-ordered starter key recoverable: it is rebuilt from the basket as stored,
        /// which is what it would have been on the patch that wrote the row.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: ChampionDimensionCanonicalKeys.SpellPairCanonicalCheckName,
                table: "champion_dim_spell_pairs");

            migrationBuilder.DropCheckConstraint(
                name: ChampionDimensionCanonicalKeys.RunePageCanonicalCheckName,
                table: "champion_dim_rune_pages");

            migrationBuilder.DropIndex(
                name: "IX_champion_dim_rune_pages_PrimaryKeystoneId",
                table: "champion_dim_rune_pages");

            migrationBuilder.Sql(
                $"""DROP INDEX "{ChampionDimensionCanonicalKeys.SpellPairCanonicalIndexName}";""");

            migrationBuilder.Sql(
                $"""DROP INDEX "{ChampionDimensionCanonicalKeys.RunePageCanonicalIndexName}";""");

            migrationBuilder.DropIndex(
                name: "IX_champion_dim_starter_items_CanonicalKey",
                table: "champion_dim_starter_items");

            migrationBuilder.DropColumn(
                name: "CanonicalKey",
                table: "champion_dim_starter_items");

            migrationBuilder.Sql(
                $"DROP FUNCTION {ChampionDimensionCanonicalKeys.StarterItemsCanonicalKeyFunctionName}(jsonb);");

            migrationBuilder.AddColumn<string>(
                name: "StarterItemsKey",
                table: "champion_dim_starter_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                """
                UPDATE champion_dim_starter_items dim
                SET "StarterItemsKey" = stored.key
                FROM (
                    SELECT d."Id", coalesce(string_agg(item, '-'), '') AS key
                    FROM champion_dim_starter_items d
                    LEFT JOIN LATERAL jsonb_array_elements_text(d."StarterItems") AS item ON TRUE
                    GROUP BY d."Id"
                ) stored
                WHERE dim."Id" = stored."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_champion_dim_starter_items_StarterItemsKey",
                table: "champion_dim_starter_items",
                column: "StarterItemsKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_dim_spell_pairs_Spell1Id_Spell2Id",
                table: "champion_dim_spell_pairs",
                columns: new[] { "Spell1Id", "Spell2Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_dim_rune_pages_PrimaryStyleId_PrimaryKeystoneId_Pr~",
                table: "champion_dim_rune_pages",
                columns: new[]
                {
                    "PrimaryStyleId", "PrimaryKeystoneId", "PrimaryPerk1Id", "PrimaryPerk2Id",
                    "PrimaryPerk3Id", "SecondaryStyleId", "SecondaryPerk1Id", "SecondaryPerk2Id",
                    "StatOffense", "StatFlex", "StatDefense"
                },
                unique: true);
        }

        /// <summary>
        /// Collapses one dimension's canonical-key groups onto a single surviving row and
        /// moves every pattern that pointed at a loser.
        /// </summary>
        /// <remarks>
        /// Three statements, in this order and for this reason:
        /// <list type="number">
        /// <item>fold: one upsert sums every loser's games and wins into the survivor's
        /// pattern row, creating it when the survivor had none. Summing across all losers
        /// of a group in one <c>GROUP BY</c> is what makes this safe for groups larger
        /// than a pair — repointing them one by one would collide on the patterns' own
        /// six-column unique index;</item>
        /// <item>delete every pattern row that pointed at a loser: its counts now live on
        /// the survivor's row;</item>
        /// <item>delete the loser dimension rows. The FK is <c>RESTRICT</c>, so this can
        /// only succeed once nothing references them — exactly the check we want. If
        /// anything still did, it throws and the migration rolls back, because a silently
        /// orphaned pattern row would corrupt a scope.</item>
        /// </list>
        /// <para>
        /// The survivor is the lowest <c>Id</c> in the group, purely for determinism: the
        /// rows are the same thing, which is the premise of the merge.
        /// <c>FIRST_VALUE</c> over an ordered window rather than <c>MIN()</c>, because
        /// Postgres has btree ordering operators for uuid but no min/max aggregate.
        /// </para>
        /// </remarks>
        private static void MergeDuplicates(
            MigrationBuilder migrationBuilder,
            string table,
            string canonicalKey,
            string patternColumn)
        {
            foreach (var statement in MergeStatements(table, canonicalKey, patternColumn))
            {
                migrationBuilder.Sql(statement);
            }
        }

        /// <summary>
        /// The merge, as statements — so the integration test can run the very SQL this
        /// migration runs. The alternative is a test that re-types it, which tests the
        /// typing.
        /// </summary>
        public static IReadOnlyList<string> MergeStatements(
            string table,
            string canonicalKey,
            string patternColumn)
        {
            var otherColumns = new[] { "ScopeId", "BuildId", "RunePageId", "SkillOrderId", "SpellPairId", "StarterItemsId" }
                .Where(column => column != patternColumn)
                .Select(column => $"\"{column}\"")
                .ToArray();

            var otherColumnList = string.Join(", ", otherColumns);
            var otherColumnsFromPattern = string.Join(", ", otherColumns.Select(column => $"p.{column}"));
            var conflictColumns = string.Join(
                ", ",
                new[] { "ScopeId", "BuildId", "RunePageId", "SkillOrderId", "SpellPairId", "StarterItemsId" }
                    .Select(column => $"\"{column}\""));

            var statements = new List<string>();

            statements.Add(
                $"""
                -- Explicitly dropped at the end rather than ON COMMIT DROP, so the statements
                -- behave the same whether they run inside the migration's transaction or
                -- one at a time under a test.
                CREATE TEMPORARY TABLE dimension_merge AS
                WITH grouped AS (
                    SELECT
                        "Id",
                        FIRST_VALUE("Id") OVER ordered AS survivor_id,
                        COUNT(*) OVER unordered AS group_size
                    FROM {table}
                    WINDOW
                        unordered AS (PARTITION BY {canonicalKey}),
                        ordered AS (PARTITION BY {canonicalKey} ORDER BY "Id")
                )
                SELECT "Id" AS loser_id, survivor_id
                FROM grouped
                WHERE group_size > 1 AND "Id" <> survivor_id;
                """);

            statements.Add(
                $"""
                INSERT INTO champion_aggregate_patterns ("Id", {otherColumnList}, "{patternColumn}", "Games", "Wins")
                SELECT gen_random_uuid(), {otherColumnsFromPattern}, m.survivor_id,
                       SUM(p."Games"), SUM(p."Wins")
                FROM champion_aggregate_patterns p
                JOIN dimension_merge m ON p."{patternColumn}" = m.loser_id
                GROUP BY {otherColumnsFromPattern}, m.survivor_id
                ON CONFLICT ({conflictColumns}) DO UPDATE
                SET "Games" = champion_aggregate_patterns."Games" + EXCLUDED."Games",
                    "Wins" = champion_aggregate_patterns."Wins" + EXCLUDED."Wins";
                """);

            statements.Add(
                $"""
                DELETE FROM champion_aggregate_patterns p
                USING dimension_merge m
                WHERE p."{patternColumn}" = m.loser_id;
                """);

            statements.Add(
                $"""
                DELETE FROM {table}
                WHERE "Id" IN (SELECT loser_id FROM dimension_merge);
                """);

            statements.Add("DROP TABLE dimension_merge;");

            return statements;
        }
    }
}
