using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMasteryToMainChampionStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MasteryLastPlayUtc",
                table: "main_champion_stats",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MasteryPoints",
                table: "main_champion_stats",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MasteryRank",
                table: "main_champion_stats",
                type: "integer",
                nullable: true);

            // Seed from the mastery read Discovery already stored on the matching
            // candidate (#1701), so the truemain score does not wait for a full
            // MainActivity pass on every account. Ladder/manual candidates only:
            // harvested ones carry no mastery (ChampionPoints = 0). The candidate's
            // rank is computed exactly like MainActivityProcess's (points desc,
            // 1-based). Last-play time is deliberately not seeded — the candidate's
            // is as old as its discovery and would print a false "last played".
            // The next mastery read overwrites all of it.
            migrationBuilder.Sql(
                """
                UPDATE main_champion_stats AS m
                SET "MasteryPoints" = c."ChampionPoints",
                    "MasteryRank" = c."ChampionRankInMasteryTop"
                FROM main_candidates AS c
                WHERE c."PlatformId" = m."PlatformId"
                  AND c."Puuid" = m."Puuid"
                  AND c."ChampionId" = m."ChampionId"
                  AND c."Source" <> 2
                  AND c."ChampionPoints" > 0
                  AND c."ChampionRankInMasteryTop" >= 1
                  AND m."MasteryPoints" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MasteryLastPlayUtc",
                table: "main_champion_stats");

            migrationBuilder.DropColumn(
                name: "MasteryPoints",
                table: "main_champion_stats");

            migrationBuilder.DropColumn(
                name: "MasteryRank",
                table: "main_champion_stats");
        }
    }
}
