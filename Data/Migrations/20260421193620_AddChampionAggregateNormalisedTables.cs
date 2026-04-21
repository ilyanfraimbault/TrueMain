using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionAggregateNormalisedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_aggregate_scopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RiotAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    GameVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    QueueId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    LastGameStartTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_aggregate_scopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_scopes_riot_accounts_RiotAccountId",
                        column: x => x.RiotAccountId,
                        principalTable: "riot_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "champion_aggregate_skill_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillOrderKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
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
                    Spell1Id = table.Column<int>(type: "integer", nullable: false),
                    Spell2Id = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
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
                    StarterItemsKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StarterItems = table.Column<string>(type: "jsonb", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
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
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformId~",
                table: "champion_aggregate_scopes",
                columns: new[] { "ChampionId", "GameVersion", "PlatformId", "QueueId" });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_scopes_RiotAccountId_ChampionId_GameVer~1",
                table: "champion_aggregate_scopes",
                columns: new[] { "RiotAccountId", "ChampionId", "GameVersion", "PlatformId", "QueueId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_scopes_RiotAccountId_ChampionId_GameVers~",
                table: "champion_aggregate_scopes",
                columns: new[] { "RiotAccountId", "ChampionId", "GameVersion", "PlatformId", "Position" });

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

            // Backfill from the wide champion_pattern_aggregates table into the
            // normalised schema. The old table stays populated for this PR; a
            // later PR drops it once the new tables have been validated.
            migrationBuilder.Sql(
                """
                INSERT INTO champion_aggregate_scopes
                    ("Id", "RiotAccountId", "ChampionId", "GameVersion", "PlatformId",
                     "QueueId", "Position", "Games", "Wins",
                     "LastGameStartTimeUtc", "AggregatedAtUtc")
                SELECT
                    gen_random_uuid(),
                    a."RiotAccountId",
                    a."ChampionId",
                    a."GameVersion",
                    a."PlatformId",
                    a."QueueId",
                    a."Position",
                    SUM(a."Games")::int,
                    SUM(a."Wins")::int,
                    MAX(a."LastGameStartTimeUtc"),
                    MAX(a."AggregatedAtUtc")
                FROM champion_pattern_aggregates a
                GROUP BY
                    a."RiotAccountId",
                    a."ChampionId",
                    a."GameVersion",
                    a."PlatformId",
                    a."QueueId",
                    a."Position";
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO champion_aggregate_spell_pairs
                    ("Id", "ScopeId", "Spell1Id", "Spell2Id", "Games", "Wins")
                SELECT
                    gen_random_uuid(),
                    s."Id",
                    a."SummonerSpell1Id",
                    a."SummonerSpell2Id",
                    SUM(a."Games")::int,
                    SUM(a."Wins")::int
                FROM champion_pattern_aggregates a
                JOIN champion_aggregate_scopes s
                  ON s."RiotAccountId" = a."RiotAccountId"
                 AND s."ChampionId" = a."ChampionId"
                 AND s."GameVersion" = a."GameVersion"
                 AND s."PlatformId" = a."PlatformId"
                 AND s."QueueId" = a."QueueId"
                 AND s."Position" = a."Position"
                GROUP BY s."Id", a."SummonerSpell1Id", a."SummonerSpell2Id";
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO champion_aggregate_skill_orders
                    ("Id", "ScopeId", "SkillOrderKey", "Games", "Wins")
                SELECT
                    gen_random_uuid(),
                    s."Id",
                    a."SkillOrderKey",
                    SUM(a."Games")::int,
                    SUM(a."Wins")::int
                FROM champion_pattern_aggregates a
                JOIN champion_aggregate_scopes s
                  ON s."RiotAccountId" = a."RiotAccountId"
                 AND s."ChampionId" = a."ChampionId"
                 AND s."GameVersion" = a."GameVersion"
                 AND s."PlatformId" = a."PlatformId"
                 AND s."QueueId" = a."QueueId"
                 AND s."Position" = a."Position"
                GROUP BY s."Id", a."SkillOrderKey";
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO champion_aggregate_starter_items
                    ("Id", "ScopeId", "StarterItemsKey", "StarterItems", "Games", "Wins")
                SELECT
                    gen_random_uuid(),
                    s."Id",
                    a."StarterItemsKey",
                    -- Rows sharing StarterItemsKey must share StarterItems by
                    -- construction (the key is string.Join("-", items)); pick any.
                    MAX(a."StarterItems"::text)::jsonb,
                    SUM(a."Games")::int,
                    SUM(a."Wins")::int
                FROM champion_pattern_aggregates a
                JOIN champion_aggregate_scopes s
                  ON s."RiotAccountId" = a."RiotAccountId"
                 AND s."ChampionId" = a."ChampionId"
                 AND s."GameVersion" = a."GameVersion"
                 AND s."PlatformId" = a."PlatformId"
                 AND s."QueueId" = a."QueueId"
                 AND s."Position" = a."Position"
                GROUP BY s."Id", a."StarterItemsKey";
                """);

            migrationBuilder.Sql(
                """
                INSERT INTO champion_aggregate_builds
                    ("Id", "ScopeId", "BootsItemId",
                     "BuildItem0", "BuildItem1", "BuildItem2", "BuildItem3",
                     "BuildItem4", "BuildItem5", "BuildItem6",
                     "Games", "Wins")
                SELECT
                    gen_random_uuid(),
                    s."Id",
                    a."BootsItemId",
                    a."BuildItem0", a."BuildItem1", a."BuildItem2", a."BuildItem3",
                    a."BuildItem4", a."BuildItem5", a."BuildItem6",
                    SUM(a."Games")::int,
                    SUM(a."Wins")::int
                FROM champion_pattern_aggregates a
                JOIN champion_aggregate_scopes s
                  ON s."RiotAccountId" = a."RiotAccountId"
                 AND s."ChampionId" = a."ChampionId"
                 AND s."GameVersion" = a."GameVersion"
                 AND s."PlatformId" = a."PlatformId"
                 AND s."QueueId" = a."QueueId"
                 AND s."Position" = a."Position"
                GROUP BY
                    s."Id",
                    a."BootsItemId",
                    a."BuildItem0", a."BuildItem1", a."BuildItem2", a."BuildItem3",
                    a."BuildItem4", a."BuildItem5", a."BuildItem6";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_aggregate_builds");

            migrationBuilder.DropTable(
                name: "champion_aggregate_skill_orders");

            migrationBuilder.DropTable(
                name: "champion_aggregate_spell_pairs");

            migrationBuilder.DropTable(
                name: "champion_aggregate_starter_items");

            migrationBuilder.DropTable(
                name: "champion_aggregate_scopes");
        }
    }
}
