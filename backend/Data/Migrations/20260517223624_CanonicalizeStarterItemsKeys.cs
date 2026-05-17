using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalizeStarterItemsKeys : Migration
    {
        // Before this migration, StarterItemsKey was built by joining the
        // starter items in purchase order (string.Join("-", StarterItems)),
        // so the same basket bought in different sequences produced different
        // keys and ended up in distinct champion_dim_starter_items rows. The
        // ingestor now emits a canonical (price-desc, id-asc) ordering; this
        // migration brings legacy data in line by merging duplicate dim rows
        // and re-keying the survivors.
        //
        // suppressTransaction: true lets the DO block COMMIT between batches
        // so locks on champion_aggregate_patterns are released regularly. The
        // procedure is idempotent (running it twice produces the same state),
        // so a partial failure is safe to re-run.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(CanonicalizeSql, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible: we can't reconstruct the per-purchase-order keys
            // we collapsed (the original sequence was already only one of many
            // valid orderings for the same basket). A no-op Down is the
            // correct shape — re-running Up against an already-canonical state
            // is a safe no-op.
        }

        internal const string CanonicalizeSql = """
            DO $migration$
            DECLARE
                duplicate_keys TEXT[];
                current_key TEXT;
                member_ids UUID[];
                winner_id_var UUID;
                new_items_var JSONB;
                keeper_id UUID;
                extra_games INT;
                extra_wins INT;
                pattern_rec RECORD;
                processed INT := 0;
                batch_size CONSTANT INT := 500;
            BEGIN
                -- Price reference for known starter items. Items not listed
                -- fall back to price 0 (they sort to the tail of the basket,
                -- tiebroken by ascending item id) — same fallback the C# sort
                -- uses when an item is missing from ItemMetadata.
                CREATE TEMP TABLE _starter_price_ref (
                    item_id INT PRIMARY KEY,
                    price INT NOT NULL
                );
                INSERT INTO _starter_price_ref(item_id, price) VALUES
                    (1001, 300),   -- Boots of Speed
                    (1054, 450),   -- Doran's Shield
                    (1055, 450),   -- Doran's Blade
                    (1056, 400),   -- Doran's Ring
                    (1057, 400),   -- legacy Doran variant
                    (1083, 450),   -- Cull
                    (2003, 50),    -- Health Potion
                    (2031, 150),   -- Refillable Potion
                    (2033, 500),   -- Corrupting Potion
                    (3070, 400),   -- Tear of the Goddess
                    (3801, 400),   -- legacy support quest item
                    (3802, 400),   -- legacy support quest item
                    (3865, 400),   -- Spellthief's Edge
                    (3866, 400),   -- Relic Shield
                    (3867, 400);   -- Steel Shoulderguards

                -- Canonical (sorted) representation for every existing dim row.
                CREATE TEMP TABLE _starter_dim_canonical (
                    dim_id UUID PRIMARY KEY,
                    new_key TEXT NOT NULL,
                    new_items JSONB NOT NULL
                );
                INSERT INTO _starter_dim_canonical(dim_id, new_key, new_items)
                SELECT
                    grouped.dim_id,
                    string_agg(item_id::text, '-' ORDER BY price DESC, item_id ASC, purchase_order ASC) AS new_key,
                    jsonb_agg(item_id ORDER BY price DESC, item_id ASC, purchase_order ASC) AS new_items
                FROM (
                    SELECT
                        d."Id" AS dim_id,
                        (item_elem.value)::int AS item_id,
                        COALESCE(p.price, 0) AS price,
                        item_elem.ordinality AS purchase_order
                    FROM champion_dim_starter_items d
                    CROSS JOIN LATERAL jsonb_array_elements(d."StarterItems") WITH ORDINALITY AS item_elem
                    LEFT JOIN _starter_price_ref p ON p.item_id = (item_elem.value)::int
                ) grouped
                GROUP BY grouped.dim_id;

                CREATE INDEX _starter_dim_canonical_new_key_idx ON _starter_dim_canonical(new_key);

                -- Snapshot every canonical key that has more than one dim row
                -- pointing at it — those groups need merging. Using FOREACH over
                -- an array (rather than a cursor) keeps the loop body free to
                -- COMMIT between iterations.
                SELECT array_agg(new_key ORDER BY new_key)
                INTO duplicate_keys
                FROM _starter_dim_canonical
                GROUP BY new_key
                HAVING COUNT(*) > 1;

                IF duplicate_keys IS NOT NULL THEN
                    FOREACH current_key IN ARRAY duplicate_keys LOOP
                        SELECT array_agg(dim_id ORDER BY dim_id)
                        INTO member_ids
                        FROM _starter_dim_canonical
                        WHERE new_key = current_key;

                        winner_id_var := member_ids[1];

                        SELECT new_items
                        INTO new_items_var
                        FROM _starter_dim_canonical
                        WHERE new_key = current_key
                        LIMIT 1;

                        -- Reconcile pattern collisions: the unique index on
                        -- (Scope, Build, Runes, Skills, Spells, Starters) forbids
                        -- two rows that share the same five-tuple after we
                        -- redirect StarterItemsId. For each colliding tuple,
                        -- pick a keeper, fold non-keeper Games/Wins into it,
                        -- delete the non-keepers, then point the keeper at the
                        -- winner dim id.
                        FOR pattern_rec IN
                            SELECT "ScopeId", "BuildId", "RunePageId", "SkillOrderId", "SpellPairId"
                            FROM champion_aggregate_patterns
                            WHERE "StarterItemsId" = ANY(member_ids)
                            GROUP BY "ScopeId", "BuildId", "RunePageId", "SkillOrderId", "SpellPairId"
                            HAVING COUNT(*) > 1
                        LOOP
                            -- PostgreSQL doesn't define MIN()/MAX() aggregates
                            -- for the uuid type, so we pick the keeper via
                            -- ORDER BY ... LIMIT 1 instead.
                            SELECT "Id"
                            INTO keeper_id
                            FROM champion_aggregate_patterns
                            WHERE "ScopeId" = pattern_rec."ScopeId"
                              AND "BuildId" = pattern_rec."BuildId"
                              AND "RunePageId" = pattern_rec."RunePageId"
                              AND "SkillOrderId" = pattern_rec."SkillOrderId"
                              AND "SpellPairId" = pattern_rec."SpellPairId"
                              AND "StarterItemsId" = ANY(member_ids)
                            ORDER BY "Id"
                            LIMIT 1;

                            SELECT
                                COALESCE(SUM("Games") FILTER (WHERE "Id" <> keeper_id), 0)::int,
                                COALESCE(SUM("Wins") FILTER (WHERE "Id" <> keeper_id), 0)::int
                            INTO extra_games, extra_wins
                            FROM champion_aggregate_patterns
                            WHERE "ScopeId" = pattern_rec."ScopeId"
                              AND "BuildId" = pattern_rec."BuildId"
                              AND "RunePageId" = pattern_rec."RunePageId"
                              AND "SkillOrderId" = pattern_rec."SkillOrderId"
                              AND "SpellPairId" = pattern_rec."SpellPairId"
                              AND "StarterItemsId" = ANY(member_ids);

                            DELETE FROM champion_aggregate_patterns
                            WHERE "ScopeId" = pattern_rec."ScopeId"
                              AND "BuildId" = pattern_rec."BuildId"
                              AND "RunePageId" = pattern_rec."RunePageId"
                              AND "SkillOrderId" = pattern_rec."SkillOrderId"
                              AND "SpellPairId" = pattern_rec."SpellPairId"
                              AND "StarterItemsId" = ANY(member_ids)
                              AND "Id" <> keeper_id;

                            UPDATE champion_aggregate_patterns
                            SET "Games" = "Games" + extra_games,
                                "Wins" = "Wins" + extra_wins,
                                "StarterItemsId" = winner_id_var
                            WHERE "Id" = keeper_id;
                        END LOOP;

                        -- Non-colliding loser pattern rows: simple FK redirect.
                        UPDATE champion_aggregate_patterns
                        SET "StarterItemsId" = winner_id_var
                        WHERE "StarterItemsId" = ANY(member_ids)
                          AND "StarterItemsId" <> winner_id_var;

                        DELETE FROM champion_dim_starter_items
                        WHERE "Id" = ANY(member_ids)
                          AND "Id" <> winner_id_var;

                        UPDATE champion_dim_starter_items
                        SET "StarterItemsKey" = current_key,
                            "StarterItems" = new_items_var
                        WHERE "Id" = winner_id_var
                          AND ("StarterItemsKey" IS DISTINCT FROM current_key
                               OR "StarterItems" IS DISTINCT FROM new_items_var);

                        processed := processed + 1;
                        IF mod(processed, batch_size) = 0 THEN
                            COMMIT;
                        END IF;
                    END LOOP;
                END IF;

                -- Final pass: single-member groups whose key is stale (no
                -- duplicates to merge, just a rename). One UPDATE is fine here
                -- — there's no unique-index hazard because each new_key in
                -- this set is held by exactly one dim row.
                UPDATE champion_dim_starter_items d
                SET "StarterItemsKey" = c.new_key,
                    "StarterItems" = c.new_items
                FROM _starter_dim_canonical c
                WHERE d."Id" = c.dim_id
                  AND (d."StarterItemsKey" IS DISTINCT FROM c.new_key
                       OR d."StarterItems" IS DISTINCT FROM c.new_items);

                DROP TABLE _starter_dim_canonical;
                DROP TABLE _starter_price_ref;
            END
            $migration$;
            """;
    }
}
