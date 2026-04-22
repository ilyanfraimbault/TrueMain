using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionAggregateRunePages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_aggregate_rune_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PrimaryStyleId = table.Column<int>(type: "integer", nullable: false),
                    PrimaryKeystoneId = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPerk1Id = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPerk2Id = table.Column<int>(type: "integer", nullable: false),
                    PrimaryPerk3Id = table.Column<int>(type: "integer", nullable: false),
                    SecondaryStyleId = table.Column<int>(type: "integer", nullable: false),
                    SecondaryPerk1Id = table.Column<int>(type: "integer", nullable: false),
                    SecondaryPerk2Id = table.Column<int>(type: "integer", nullable: false),
                    StatOffense = table.Column<int>(type: "integer", nullable: false),
                    StatFlex = table.Column<int>(type: "integer", nullable: false),
                    StatDefense = table.Column<int>(type: "integer", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_rune_pages_ScopeId",
                table: "champion_aggregate_rune_pages",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_rune_pages_ScopeId_PrimaryStyleId_Primar~",
                table: "champion_aggregate_rune_pages",
                columns: new[] { "ScopeId", "PrimaryStyleId", "PrimaryKeystoneId", "PrimaryPerk1Id", "PrimaryPerk2Id", "PrimaryPerk3Id", "SecondaryStyleId", "SecondaryPerk1Id", "SecondaryPerk2Id", "StatOffense", "StatFlex", "StatDefense" },
                unique: true);

            // Backfill rune pages from the source of truth (match participants
            // + their perk selections). The legacy champion_pattern_aggregates
            // table never stored the 6 individual perk ids, so we have to
            // rebuild rune pages by pivoting participant_perk_selections ⋈
            // perk_selection_catalogs in SQL. ON CONFLICT DO NOTHING keeps the
            // statement idempotent on re-runs.
            migrationBuilder.Sql(
                """
                WITH participant_rune_pages AS (
                    SELECT
                        p."Id"             AS participant_id,
                        p."RiotAccountId"  AS riot_account_id,
                        p."ChampionId"     AS champion_id,
                        split_part(m."GameVersion", '.', 1)
                            || '.' || split_part(m."GameVersion", '.', 2)
                            AS game_version,
                        m."PlatformId"     AS platform_id,
                        m."QueueId"        AS queue_id,
                        UPPER(TRIM(p."TeamPosition")) AS position,
                        p."PrimaryStyleId" AS primary_style_id,
                        p."SubStyleId"     AS secondary_style_id,
                        p."PerksOffense"   AS stat_offense,
                        p."PerksFlex"      AS stat_flex,
                        p."PerksDefense"   AS stat_defense,
                        p."Win"            AS win,
                        MAX(CASE WHEN c."StyleDescription" = 'primaryStyle' AND c."SelectionIndex" = 0
                                THEN c."PerkId" ELSE 0 END) AS primary_keystone_id,
                        MAX(CASE WHEN c."StyleDescription" = 'primaryStyle' AND c."SelectionIndex" = 1
                                THEN c."PerkId" ELSE 0 END) AS primary_perk_1_id,
                        MAX(CASE WHEN c."StyleDescription" = 'primaryStyle' AND c."SelectionIndex" = 2
                                THEN c."PerkId" ELSE 0 END) AS primary_perk_2_id,
                        MAX(CASE WHEN c."StyleDescription" = 'primaryStyle' AND c."SelectionIndex" = 3
                                THEN c."PerkId" ELSE 0 END) AS primary_perk_3_id,
                        MAX(CASE WHEN c."StyleDescription" = 'subStyle' AND c."SelectionIndex" = 0
                                THEN c."PerkId" ELSE 0 END) AS secondary_perk_1_id,
                        MAX(CASE WHEN c."StyleDescription" = 'subStyle' AND c."SelectionIndex" = 1
                                THEN c."PerkId" ELSE 0 END) AS secondary_perk_2_id
                    FROM match_participants p
                    JOIN matches m ON p."MatchId" = m."Id"
                    JOIN main_champion_stats s
                        ON s."PlatformId" = m."PlatformId"
                       AND s."Puuid"      = p."Puuid"
                       AND s."ChampionId" = p."ChampionId"
                    JOIN participant_perk_selections pps
                        ON pps."MatchId"       = p."MatchId"
                       AND pps."ParticipantId" = p."ParticipantId"
                    JOIN perk_selection_catalogs c
                        ON c."Id" = pps."PerkSelectionCatalogId"
                    WHERE s."IsMain" = TRUE
                      AND p."RiotAccountId" IS NOT NULL
                      AND m."TimelineIngested" = TRUE
                      AND UPPER(TRIM(p."TeamPosition"))
                          IN ('TOP','JUNGLE','MIDDLE','BOTTOM','UTILITY')
                    GROUP BY
                        p."Id", p."RiotAccountId", p."ChampionId",
                        m."GameVersion", m."PlatformId", m."QueueId",
                        p."TeamPosition",
                        p."PrimaryStyleId", p."SubStyleId",
                        p."PerksOffense", p."PerksFlex", p."PerksDefense",
                        p."Win"
                )
                INSERT INTO champion_aggregate_rune_pages (
                    "Id", "ScopeId",
                    "PrimaryStyleId", "PrimaryKeystoneId",
                    "PrimaryPerk1Id", "PrimaryPerk2Id", "PrimaryPerk3Id",
                    "SecondaryStyleId", "SecondaryPerk1Id", "SecondaryPerk2Id",
                    "StatOffense", "StatFlex", "StatDefense",
                    "Games", "Wins"
                )
                SELECT
                    gen_random_uuid(),
                    scope."Id",
                    prp.primary_style_id,
                    prp.primary_keystone_id,
                    prp.primary_perk_1_id,
                    prp.primary_perk_2_id,
                    prp.primary_perk_3_id,
                    prp.secondary_style_id,
                    prp.secondary_perk_1_id,
                    prp.secondary_perk_2_id,
                    prp.stat_offense,
                    prp.stat_flex,
                    prp.stat_defense,
                    COUNT(*)::int,
                    (COUNT(*) FILTER (WHERE prp.win))::int
                FROM participant_rune_pages prp
                JOIN champion_aggregate_scopes scope
                    ON scope."RiotAccountId" = prp.riot_account_id
                   AND scope."ChampionId"    = prp.champion_id
                   AND scope."GameVersion"   = prp.game_version
                   AND scope."PlatformId"    = prp.platform_id
                   AND scope."QueueId"       = prp.queue_id
                   AND scope."Position"      = prp.position
                GROUP BY
                    scope."Id",
                    prp.primary_style_id, prp.primary_keystone_id,
                    prp.primary_perk_1_id, prp.primary_perk_2_id, prp.primary_perk_3_id,
                    prp.secondary_style_id, prp.secondary_perk_1_id, prp.secondary_perk_2_id,
                    prp.stat_offense, prp.stat_flex, prp.stat_defense
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_aggregate_rune_pages");
        }
    }
}
