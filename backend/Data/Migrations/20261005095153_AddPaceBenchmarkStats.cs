using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaceBenchmarkStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PaceBenchmarkAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "pace_benchmark_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Minute = table.Column<int>(type: "integer", nullable: false),
                    Metric = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Bucket = table.Column<int>(type: "integer", nullable: false),
                    Count = table.Column<long>(type: "bigint", nullable: false),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pace_benchmark_stats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pace_benchmark_stats_grain",
                table: "pace_benchmark_stats",
                columns: new[] { "Position", "Patch", "Tier", "Minute", "Metric", "Bucket" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pace_benchmark_stats");

            migrationBuilder.DropColumn(
                name: "PaceBenchmarkAggregated",
                table: "matches");
        }
    }
}
