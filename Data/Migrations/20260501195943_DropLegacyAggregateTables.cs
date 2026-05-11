using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyAggregateTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Phase 6.4 — backfill the new pattern junction from the legacy
            // wide table BEFORE dropping it. Each ChampionPatternAggregate
            // row maps to one ChampionAggregatePattern row joined to the
            // five new dim tables.
            //
            // Rune-page caveat: the legacy table only stored style ids and
            // stat shards — the keystone + 3 primary perks + 2 secondary
            // perks live in participant_perk_selections and were not
            // denormalised onto the wide row. We backfill one rune-page dim
            // per (PrimaryStyleId, SubStyleId, stats) tuple with keystone
            // and perks set to 0 so the read side keeps tree-level info
            // (Precision vs Domination) at minimum. The next
            // ChampionPatternAggregationProcess run replaces each scope's
            // patterns with proper full-perk rune pages via cascade — the
            // aggregator already pulls keystone + perks out of
            // participant_perk_selections.
            //
            // ON CONFLICT DO NOTHING keeps the backfill idempotent if the
            // dual-write from PR 6.2 already populated some rows.
            migrationBuilder.Sql(
                """
                -- Backfill dim builds (deduplicated)
                INSERT INTO champion_dim_builds (
                    "Id", "BootsItemId", "BuildItem0", "BuildItem1", "BuildItem2",
                    "BuildItem3", "BuildItem4", "BuildItem5", "BuildItem6"
                )
                SELECT DISTINCT ON (
                    "BootsItemId", "BuildItem0", "BuildItem1", "BuildItem2",
                    "BuildItem3", "BuildItem4", "BuildItem5", "BuildItem6"
                )
                    gen_random_uuid(),
                    "BootsItemId", "BuildItem0", "BuildItem1", "BuildItem2",
                    "BuildItem3", "BuildItem4", "BuildItem5", "BuildItem6"
                FROM champion_pattern_aggregates
                ON CONFLICT DO NOTHING;

                -- Backfill dim skill orders
                INSERT INTO champion_dim_skill_orders ("Id", "SkillOrderKey")
                SELECT gen_random_uuid(), key
                FROM (SELECT DISTINCT "SkillOrderKey" AS key FROM champion_pattern_aggregates) src
                ON CONFLICT DO NOTHING;

                -- Backfill dim spell pairs
                INSERT INTO champion_dim_spell_pairs ("Id", "Spell1Id", "Spell2Id")
                SELECT gen_random_uuid(), spell1, spell2
                FROM (
                    SELECT DISTINCT "SummonerSpell1Id" AS spell1, "SummonerSpell2Id" AS spell2
                    FROM champion_pattern_aggregates
                ) src
                ON CONFLICT DO NOTHING;

                -- Backfill dim starter items
                INSERT INTO champion_dim_starter_items ("Id", "StarterItemsKey", "StarterItems")
                SELECT DISTINCT ON (src."StarterItemsKey")
                    gen_random_uuid(), src."StarterItemsKey", src."StarterItems"
                FROM champion_pattern_aggregates src
                ON CONFLICT DO NOTHING;

                -- Backfill dim rune pages: one per (style, sub-style, stats)
                -- tuple. Keystone + perks default to 0 — see migration comment.
                INSERT INTO champion_dim_rune_pages (
                    "Id",
                    "PrimaryStyleId", "PrimaryKeystoneId",
                    "PrimaryPerk1Id", "PrimaryPerk2Id", "PrimaryPerk3Id",
                    "SecondaryStyleId", "SecondaryPerk1Id", "SecondaryPerk2Id",
                    "StatOffense", "StatFlex", "StatDefense"
                )
                SELECT DISTINCT ON ("PrimaryStyleId", "SubStyleId", "PerksOffense", "PerksFlex", "PerksDefense")
                    gen_random_uuid(),
                    "PrimaryStyleId", 0,
                    0, 0, 0,
                    "SubStyleId", 0, 0,
                    "PerksOffense", "PerksFlex", "PerksDefense"
                FROM champion_pattern_aggregates
                ON CONFLICT DO NOTHING;

                -- Backfill patterns: join the legacy row to every dim by its
                -- natural key. ON CONFLICT DO NOTHING handles patterns the
                -- dual-write already wrote.
                INSERT INTO champion_aggregate_patterns (
                    "Id", "ScopeId", "BuildId", "RunePageId", "SkillOrderId",
                    "SpellPairId", "StarterItemsId", "Games", "Wins"
                )
                SELECT
                    gen_random_uuid(),
                    scope."Id",
                    dim_build."Id",
                    dim_rune."Id",
                    dim_skill."Id",
                    dim_spell."Id",
                    dim_starter."Id",
                    legacy."Games",
                    legacy."Wins"
                FROM champion_pattern_aggregates legacy
                JOIN champion_aggregate_scopes scope
                    ON scope."RiotAccountId" = legacy."RiotAccountId"
                   AND scope."ChampionId"    = legacy."ChampionId"
                   AND scope."GameVersion"   = legacy."GameVersion"
                   AND scope."PlatformId"    = legacy."PlatformId"
                   AND scope."QueueId"       = legacy."QueueId"
                   AND scope."Position"      = legacy."Position"
                JOIN champion_dim_builds dim_build
                    ON dim_build."BootsItemId" = legacy."BootsItemId"
                   AND dim_build."BuildItem0"  = legacy."BuildItem0"
                   AND dim_build."BuildItem1"  = legacy."BuildItem1"
                   AND dim_build."BuildItem2"  = legacy."BuildItem2"
                   AND dim_build."BuildItem3"  = legacy."BuildItem3"
                   AND dim_build."BuildItem4"  = legacy."BuildItem4"
                   AND dim_build."BuildItem5"  = legacy."BuildItem5"
                   AND dim_build."BuildItem6"  = legacy."BuildItem6"
                JOIN champion_dim_skill_orders dim_skill
                    ON dim_skill."SkillOrderKey" = legacy."SkillOrderKey"
                JOIN champion_dim_spell_pairs dim_spell
                    ON dim_spell."Spell1Id" = legacy."SummonerSpell1Id"
                   AND dim_spell."Spell2Id" = legacy."SummonerSpell2Id"
                JOIN champion_dim_starter_items dim_starter
                    ON dim_starter."StarterItemsKey" = legacy."StarterItemsKey"
                JOIN champion_dim_rune_pages dim_rune
                    ON dim_rune."PrimaryStyleId"   = legacy."PrimaryStyleId"
                   AND dim_rune."SecondaryStyleId" = legacy."SubStyleId"
                   AND dim_rune."StatOffense"      = legacy."PerksOffense"
                   AND dim_rune."StatFlex"         = legacy."PerksFlex"
                   AND dim_rune."StatDefense"      = legacy."PerksDefense"
                   AND dim_rune."PrimaryKeystoneId" = 0
                   AND dim_rune."PrimaryPerk1Id"    = 0
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.DropTable(
                name: "champion_aggregate_builds");

            migrationBuilder.DropTable(
                name: "champion_aggregate_rune_pages");

            migrationBuilder.DropTable(
                name: "champion_aggregate_skill_orders");

            migrationBuilder.DropTable(
                name: "champion_aggregate_spell_pairs");

            migrationBuilder.DropTable(
                name: "champion_aggregate_starter_items");

            migrationBuilder.DropTable(
                name: "champion_pattern_aggregates");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_aggregate_builds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BootsItemId = table.Column<int>(type: "integer", nullable: false),
                    BuildItem0 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem1 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem2 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem3 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem4 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem5 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem6 = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_aggregate_builds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_builds_champion_aggregate_scopes_ScopeId",
                        column: x => x.ScopeId,
                        principalTable: "champion_aggregate_scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "champion_aggregate_rune_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstItemId = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    PrimaryKeystoneId = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPerk1Id = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPerk2Id = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPerk3Id = table.Column<int>(type: "integer", nullable: false),
                    PrimaryStyleId = table.Column<int>(type: "integer", nullable: false),
                    SecondaryPerk1Id = table.Column<int>(type: "integer", nullable: false),
                    SecondaryPerk2Id = table.Column<int>(type: "integer", nullable: false),
                    SecondaryStyleId = table.Column<int>(type: "integer", nullable: false),
                    StatDefense = table.Column<int>(type: "integer", nullable: false),
                    StatFlex = table.Column<int>(type: "integer", nullable: false),
                    StatOffense = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_aggregate_rune_pages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_rune_pages_champion_aggregate_scopes_Sco~",
                        column: x => x.ScopeId,
                        principalTable: "champion_aggregate_scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "champion_aggregate_skill_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    SkillOrderKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_aggregate_skill_orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_skill_orders_champion_aggregate_scopes_S~",
                        column: x => x.ScopeId,
                        principalTable: "champion_aggregate_scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "champion_aggregate_spell_pairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Spell1Id = table.Column<int>(type: "integer", nullable: false),
                    Spell2Id = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_aggregate_spell_pairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_spell_pairs_champion_aggregate_scopes_Sc~",
                        column: x => x.ScopeId,
                        principalTable: "champion_aggregate_scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "champion_aggregate_starter_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    StarterItems = table.Column<string>(type: "jsonb", nullable: false),
                    StarterItemsKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_aggregate_starter_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_starter_items_champion_aggregate_scopes_~",
                        column: x => x.ScopeId,
                        principalTable: "champion_aggregate_scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "champion_pattern_aggregates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RiotAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BootsItemId = table.Column<int>(type: "integer", nullable: false),
                    BuildItem0 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem1 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem2 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem3 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem4 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem5 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem6 = table.Column<int>(type: "integer", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    GameVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    LastGameStartTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PerksDefense = table.Column<int>(type: "integer", nullable: false),
                    PerksFlex = table.Column<int>(type: "integer", nullable: false),
                    PerksOffense = table.Column<int>(type: "integer", nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PrimaryStyleId = table.Column<int>(type: "integer", nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    SkillOrderKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StarterItems = table.Column<string>(type: "jsonb", nullable: false),
                    StarterItemsKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubStyleId = table.Column<int>(type: "integer", nullable: false),
                    SummonerSpell1Id = table.Column<int>(type: "integer", nullable: false),
                    SummonerSpell2Id = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_pattern_aggregates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_champion_pattern_aggregates_riot_accounts_RiotAccountId",
                        column: x => x.RiotAccountId,
                        principalTable: "riot_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_builds_ScopeId",
                table: "champion_aggregate_builds",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_builds_ScopeId_BootsItemId_BuildItem0_Bu~",
                table: "champion_aggregate_builds",
                columns: new[] { "ScopeId", "BootsItemId", "BuildItem0", "BuildItem1", "BuildItem2", "BuildItem3", "BuildItem4", "BuildItem5", "BuildItem6" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_rune_pages_ScopeId",
                table: "champion_aggregate_rune_pages",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_rune_pages_ScopeId_FirstItemId",
                table: "champion_aggregate_rune_pages",
                columns: new[] { "ScopeId", "FirstItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_rune_pages_ScopeId_FirstItemId_PrimarySt~",
                table: "champion_aggregate_rune_pages",
                columns: new[] { "ScopeId", "FirstItemId", "PrimaryStyleId", "PrimaryKeystoneId", "PrimaryPerk1Id", "PrimaryPerk2Id", "PrimaryPerk3Id", "SecondaryStyleId", "SecondaryPerk1Id", "SecondaryPerk2Id", "StatOffense", "StatFlex", "StatDefense" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_skill_orders_ScopeId",
                table: "champion_aggregate_skill_orders",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_skill_orders_ScopeId_SkillOrderKey",
                table: "champion_aggregate_skill_orders",
                columns: new[] { "ScopeId", "SkillOrderKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_spell_pairs_ScopeId",
                table: "champion_aggregate_spell_pairs",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_spell_pairs_ScopeId_Spell1Id_Spell2Id",
                table: "champion_aggregate_spell_pairs",
                columns: new[] { "ScopeId", "Spell1Id", "Spell2Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_starter_items_ScopeId",
                table: "champion_aggregate_starter_items",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_starter_items_ScopeId_StarterItemsKey",
                table: "champion_aggregate_starter_items",
                columns: new[] { "ScopeId", "StarterItemsKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_pattern_aggregates_RiotAccountId_ChampionId_GameV~1",
                table: "champion_pattern_aggregates",
                columns: new[] { "RiotAccountId", "ChampionId", "GameVersion", "PlatformId", "QueueId", "Position", "PrimaryStyleId", "SubStyleId", "PerksOffense", "PerksFlex", "PerksDefense", "SummonerSpell1Id", "SummonerSpell2Id", "SkillOrderKey", "StarterItemsKey", "BootsItemId", "BuildItem0", "BuildItem1", "BuildItem2", "BuildItem3", "BuildItem4", "BuildItem5", "BuildItem6" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_pattern_aggregates_RiotAccountId_ChampionId_GameVe~",
                table: "champion_pattern_aggregates",
                columns: new[] { "RiotAccountId", "ChampionId", "GameVersion", "PlatformId", "Position" });
        }
    }
}
