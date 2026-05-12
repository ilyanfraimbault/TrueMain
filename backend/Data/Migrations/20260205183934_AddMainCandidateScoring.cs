using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMainCandidateScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Score",
                table: "main_candidates",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScoredAtUtc",
                table: "main_candidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "main_candidates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_main_candidates_PlatformId_Status_Score",
                table: "main_candidates",
                columns: new[] { "PlatformId", "Status", "Score" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_main_candidates_PlatformId_Status_Score",
                table: "main_candidates");

            migrationBuilder.DropColumn(
                name: "Score",
                table: "main_candidates");

            migrationBuilder.DropColumn(
                name: "ScoredAtUtc",
                table: "main_candidates");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "main_candidates");
        }
    }
}
