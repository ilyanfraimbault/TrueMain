using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class PurgeStarterClassItemsFromBuilds : Migration
    {
        // Before this migration, FinalBuildResolver only filtered Trinkets +
        // Cull (hardcoded) from the build path. Starter-class items bought
        // outside StarterItemAnalyzer's 120s window — most commonly Doran's
        // when a player started Crystal+Refillable then bought a Doran on a
        // later back — leaked into BuildItem0..6. The ingestor now flags those
        // items via ItemMetadata.IsStarterClassItem (detected dynamically per
        // patch from CommunityDragon: Lane/Jungle category + no recipe + no
        // upgrade + cheap + in-store + non-consumable + non-boots). This
        // migration scrubs the legacy buggy patterns so the API doesn't keep
        // surfacing them until the worker's next aggregation tick.
        //
        // Item IDs below are the snapshot of all items matching the dynamic
        // discriminator on patches present in the DB at the time of writing
        // (16.x). Hardcoding here is acceptable for a one-shot data fix —
        // production code uses the dynamic detector, so any new starter-class
        // item Riot adds in a future patch is caught by the worker without
        // touching this migration.
        //
        // Strategy:
        //   1. DELETE every champion_aggregate_patterns row whose BuildId
        //      points to a champion_dim_builds row containing a starter-class
        //      ID in any of BuildItem0..6.
        //   2. DELETE the now-orphan champion_dim_builds rows (FK is RESTRICT
        //      so pattern cleanup has to land first).
        // suppressTransaction lets the DO block COMMIT between batches so
        // pattern-table locks are released regularly. The procedure is
        // idempotent — running twice is a safe no-op on a clean state.
        //
        // The worker re-aggregates each affected scope on its next tick via
        // ChampionPatternAggregatePersister.ReplaceAggregatesAsync (delete-by-
        // scope + re-insert with the corrected FinalBuildResolver). No manual
        // trigger needed.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(PurgeSql, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Forward-only: re-aggregation rebuilds correct patterns on the
            // next worker tick. Reverting would mean re-introducing the bug.
        }

        internal const string PurgeSql = """
            DO $migration$
            DECLARE
                affected_pattern_ids UUID[];
                batch UUID[];
                deleted INT;
                batch_size CONSTANT INT := 500;
            BEGIN
                -- Snapshot of starter-class IDs at the time of this migration,
                -- derived from the dynamic discriminator on patches 16.x:
                --   inStore=true && from=[] && to=[] && priceTotal>0 && <600
                --   && categories contains "Lane" or "Jungle"
                --   && !consumable && !boots
                CREATE TEMP TABLE _starter_class_ids (item_id INT PRIMARY KEY);
                INSERT INTO _starter_class_ids(item_id) VALUES
                    (1040),   -- Obsidian Edge
                    (1054),   -- Doran's Shield
                    (1055),   -- Doran's Blade
                    (1056),   -- Doran's Ring
                    (1083),   -- Cull
                    (1086),   -- Doran's Bow
                    (1101),   -- Scorchclaw Pup
                    (1102),   -- Gustwalker Hatchling
                    (1103),   -- Mosstomper Seedling
                    (1105),   -- Mosstomper Seedling (variant)
                    (1106),   -- Gustwalker Hatchling (Smite variant)
                    (1107),   -- Scorchclaw Pup (variant)
                    (1120),   -- Doran's Helm
                    (2049),   -- Guardian's Amulet (ARAM)
                    (2050),   -- Guardian's Shroud (ARAM)
                    (222051), -- Guardian's Horn (ARAM)
                    (223112), -- Guardian's Orb (ARAM)
                    (223177), -- Guardian's Blade (ARAM)
                    (223184), -- Guardian's Hammer (ARAM)
                    (223185); -- Guardian's Dirk (ARAM)

                -- Materialise the pattern ids to delete. Doing one big DELETE
                -- in the EXISTS-with-OR form is fine for a few thousand rows
                -- but holds a long lock on champion_aggregate_patterns; we
                -- batch via array slicing instead.
                SELECT array_agg(p."Id" ORDER BY p."Id")
                INTO affected_pattern_ids
                FROM champion_aggregate_patterns p
                JOIN champion_dim_builds b ON b."Id" = p."BuildId"
                WHERE b."BuildItem0" IN (SELECT item_id FROM _starter_class_ids)
                   OR b."BuildItem1" IN (SELECT item_id FROM _starter_class_ids)
                   OR b."BuildItem2" IN (SELECT item_id FROM _starter_class_ids)
                   OR b."BuildItem3" IN (SELECT item_id FROM _starter_class_ids)
                   OR b."BuildItem4" IN (SELECT item_id FROM _starter_class_ids)
                   OR b."BuildItem5" IN (SELECT item_id FROM _starter_class_ids)
                   OR b."BuildItem6" IN (SELECT item_id FROM _starter_class_ids);

                IF affected_pattern_ids IS NOT NULL THEN
                    FOR i IN 1 .. array_length(affected_pattern_ids, 1) BY batch_size LOOP
                        batch := affected_pattern_ids[i : LEAST(i + batch_size - 1, array_length(affected_pattern_ids, 1))];
                        DELETE FROM champion_aggregate_patterns
                        WHERE "Id" = ANY(batch);
                        GET DIAGNOSTICS deleted = ROW_COUNT;
                        COMMIT;
                    END LOOP;
                END IF;

                -- Orphan dim_builds rows: champion_dim_builds → FK from
                -- patterns is ON DELETE RESTRICT, so we can only drop the
                -- build rows that have no remaining patterns pointing at
                -- them. The NOT EXISTS clause guards against builds shared
                -- with a (hypothetical) legitimate pattern that doesn't carry
                -- a starter-class item.
                DELETE FROM champion_dim_builds b
                WHERE (b."BuildItem0" IN (SELECT item_id FROM _starter_class_ids)
                       OR b."BuildItem1" IN (SELECT item_id FROM _starter_class_ids)
                       OR b."BuildItem2" IN (SELECT item_id FROM _starter_class_ids)
                       OR b."BuildItem3" IN (SELECT item_id FROM _starter_class_ids)
                       OR b."BuildItem4" IN (SELECT item_id FROM _starter_class_ids)
                       OR b."BuildItem5" IN (SELECT item_id FROM _starter_class_ids)
                       OR b."BuildItem6" IN (SELECT item_id FROM _starter_class_ids))
                  AND NOT EXISTS (
                      SELECT 1 FROM champion_aggregate_patterns p
                      WHERE p."BuildId" = b."Id"
                  );

                DROP TABLE _starter_class_ids;
            END
            $migration$;
            """;
    }
}
