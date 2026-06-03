using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHotPathIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_riot_accounts_ingest_claim_lease",
                table: "riot_accounts");

            migrationBuilder.CreateIndex(
                name: "IX_riot_accounts_ingest_claim_lease",
                table: "riot_accounts",
                columns: new[] { "MatchIngestStatus", "MatchIngestClaimedAtUtc", "LastMatchIngestAtUtc" },
                filter: "\"MatchIngestStatus\" <> 0");

            migrationBuilder.CreateIndex(
                name: "IX_main_champion_stats_PlatformId_IsMain",
                table: "main_champion_stats",
                columns: new[] { "PlatformId", "IsMain" })
                .Annotation("Npgsql:IndexInclude", new[] { "Puuid" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_riot_accounts_ingest_claim_lease",
                table: "riot_accounts");

            migrationBuilder.DropIndex(
                name: "IX_main_champion_stats_PlatformId_IsMain",
                table: "main_champion_stats");

            migrationBuilder.CreateIndex(
                name: "IX_riot_accounts_ingest_claim_lease",
                table: "riot_accounts",
                columns: new[] { "MatchIngestStatus", "MatchIngestClaimedAtUtc", "LastMatchIngestAtUtc" });
        }
    }
}
