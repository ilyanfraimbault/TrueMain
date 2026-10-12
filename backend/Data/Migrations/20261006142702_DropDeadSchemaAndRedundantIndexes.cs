using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Drops the dead schema and two redundant indexes (#1244). Plain transactional DDL, catalog-only
    /// (no table rewrite, no index build), no <c>CONCURRENTLY</c> (#1227).
    /// <list type="bullet">
    /// <item><c>process_runs</c> and <c>seed_requests</c>: frozen since both moved to the Mongo collections of
    /// the same names; nothing reads or writes the Postgres tables any more.</item>
    /// <item><c>personas</c>, <c>riot_accounts."PersonaId"</c>, its FK and <c>IX_riot_accounts_PersonaId</c>: never
    /// had a writer or a reader (product decision on #1244).</item>
    /// <item><c>players</c>, if present: the pre-rename name of <c>riot_accounts</c>
    /// (<c>RenamePlayersToRiotAccounts</c>), outside the EF model. <c>IF EXISTS</c> and no <c>CASCADE</c>, so a
    /// database without it is untouched and one where something still depends on it fails instead of
    /// cascading.</item>
    /// <item><c>IX_matches_PlatformId</c>: a strict prefix of <c>IX_matches_platform_queue_game_start</c>.</item>
    /// <item><c>(ChampionId, GameVersion, PlatformId, QueueId)</c> on <c>champion_aggregate_scopes</c>: a strict
    /// prefix of the reader index <c>(ChampionId, GameVersion, PlatformId, QueueId, Position, EloBracket,
    /// IsMain)</c>, which EF then renames into the freed truncated name.</item>
    /// </list>
    /// </summary>
    public partial class DropDeadSchemaAndRedundantIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_riot_accounts_personas_PersonaId",
                table: "riot_accounts");

            migrationBuilder.DropTable(
                name: "personas");

            migrationBuilder.DropTable(
                name: "process_runs");

            migrationBuilder.DropTable(
                name: "seed_requests");

            migrationBuilder.Sql("DROP TABLE IF EXISTS players;");

            migrationBuilder.DropIndex(
                name: "IX_riot_accounts_PersonaId",
                table: "riot_accounts");

            migrationBuilder.DropIndex(
                name: "IX_matches_PlatformId",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformId~",
                table: "champion_aggregate_scopes");

            migrationBuilder.DropColumn(
                name: "PersonaId",
                table: "riot_accounts");

            migrationBuilder.RenameIndex(
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformI~1",
                table: "champion_aggregate_scopes",
                newName: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformId~");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreates the dropped structure empty; the rows are gone, and the stray players table
            // (never in the EF model) is not recreated.
            migrationBuilder.RenameIndex(
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformId~",
                table: "champion_aggregate_scopes",
                newName: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformI~1");

            migrationBuilder.AddColumn<Guid>(
                name: "PersonaId",
                table: "riot_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "personas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "process_runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Host = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IterationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastHeartbeatAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Summary = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_process_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "seed_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Error = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    GameName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedPuuid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ResolvedRiotAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TagLine = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_seed_requests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_riot_accounts_PersonaId",
                table: "riot_accounts",
                column: "PersonaId");

            migrationBuilder.CreateIndex(
                name: "IX_matches_PlatformId",
                table: "matches",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_scopes_ChampionId_GameVersion_PlatformId~",
                table: "champion_aggregate_scopes",
                columns: new[] { "ChampionId", "GameVersion", "PlatformId", "QueueId" });

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_IterationId_StartedAtUtc",
                table: "process_runs",
                columns: new[] { "IterationId", "StartedAtUtc" },
                filter: "\"IterationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_process_runs_ProcessName_StartedAtUtc",
                table: "process_runs",
                columns: new[] { "ProcessName", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_seed_requests_RequestedAtUtc",
                table: "seed_requests",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_seed_requests_Status",
                table: "seed_requests",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_riot_accounts_personas_PersonaId",
                table: "riot_accounts",
                column: "PersonaId",
                principalTable: "personas",
                principalColumn: "Id");
        }
    }
}
