using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionProfileStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ProfileAggregated",
                table: "matches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "champion_profile_stats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChampionId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Patch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    GameDurationSecondsSum = table.Column<long>(type: "bigint", nullable: false),
                    PhysicalDamageToChampionsSum = table.Column<long>(type: "bigint", nullable: false),
                    MagicDamageToChampionsSum = table.Column<long>(type: "bigint", nullable: false),
                    TrueDamageToChampionsSum = table.Column<long>(type: "bigint", nullable: false),
                    TotalHealSum = table.Column<long>(type: "bigint", nullable: false),
                    HealsOnTeammatesSum = table.Column<long>(type: "bigint", nullable: false),
                    DamageShieldedOnTeammatesSum = table.Column<long>(type: "bigint", nullable: false),
                    TimeCCingOthersSum = table.Column<long>(type: "bigint", nullable: false),
                    TotalTimeCCDealtSum = table.Column<long>(type: "bigint", nullable: false),
                    DamageTakenSum = table.Column<long>(type: "bigint", nullable: false),
                    DamageSelfMitigatedSum = table.Column<long>(type: "bigint", nullable: false),
                    TeamDamageTakenGames = table.Column<int>(type: "integer", nullable: false),
                    TeamDamageTakenSum = table.Column<long>(type: "bigint", nullable: false),
                    LaneGamesAt10 = table.Column<int>(type: "integer", nullable: false),
                    GoldLeadAt10Sum = table.Column<long>(type: "bigint", nullable: false),
                    XpLeadAt10Sum = table.Column<long>(type: "bigint", nullable: false),
                    KillsBy10Sum = table.Column<int>(type: "integer", nullable: false),
                    LaneGamesAt15 = table.Column<int>(type: "integer", nullable: false),
                    GoldLeadAt15Sum = table.Column<long>(type: "bigint", nullable: false),
                    XpLeadAt15Sum = table.Column<long>(type: "bigint", nullable: false),
                    ItemGames = table.Column<int>(type: "integer", nullable: false),
                    CritGames = table.Column<int>(type: "integer", nullable: false),
                    ArmorPenetrationGames = table.Column<int>(type: "integer", nullable: false),
                    OnHitGames = table.Column<int>(type: "integer", nullable: false),
                    AbilityPowerGames = table.Column<int>(type: "integer", nullable: false),
                    TankGames = table.Column<int>(type: "integer", nullable: false),
                    IsRanged = table.Column<bool>(type: "boolean", nullable: true),
                    AggregatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_profile_stats", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_profile_pending",
                table: "matches",
                column: "QueueId",
                filter: "\"ProfileAggregated\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_champion_profile_stats_grain",
                table: "champion_profile_stats",
                columns: new[] { "Patch", "ChampionId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_profile_stats");

            migrationBuilder.DropIndex(
                name: "IX_matches_profile_pending",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "ProfileAggregated",
                table: "matches");
        }
    }
}
