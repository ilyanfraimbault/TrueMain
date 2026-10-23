using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonaConflicts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoPersonaId",
                table: "personas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "persona_account_conflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimantPersonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerPersonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TagLine = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RiotAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FirstSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TimesSeen = table.Column<int>(type: "integer", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persona_account_conflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_persona_account_conflicts_personas_ClaimantPersonaId",
                        column: x => x.ClaimantPersonaId,
                        principalTable: "personas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_persona_account_conflicts_personas_OwnerPersonaId",
                        column: x => x.OwnerPersonaId,
                        principalTable: "personas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_persona_account_conflicts_riot_accounts_RiotAccountId",
                        column: x => x.RiotAccountId,
                        principalTable: "riot_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "persona_account_exclusions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TagLine = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RiotAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ConflictId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persona_account_exclusions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_persona_account_exclusions_persona_account_conflicts_Confli~",
                        column: x => x.ConflictId,
                        principalTable: "persona_account_conflicts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_persona_account_exclusions_personas_PersonaId",
                        column: x => x.PersonaId,
                        principalTable: "personas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_persona_account_exclusions_riot_accounts_RiotAccountId",
                        column: x => x.RiotAccountId,
                        principalTable: "riot_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_personas_MergedIntoPersonaId",
                table: "personas",
                column: "MergedIntoPersonaId");

            migrationBuilder.CreateIndex(
                name: "IX_persona_account_conflicts_ClaimantPersonaId",
                table: "persona_account_conflicts",
                column: "ClaimantPersonaId");

            migrationBuilder.CreateIndex(
                name: "IX_persona_account_conflicts_OwnerPersonaId",
                table: "persona_account_conflicts",
                column: "OwnerPersonaId");

            migrationBuilder.CreateIndex(
                name: "IX_persona_account_conflicts_RiotAccountId",
                table: "persona_account_conflicts",
                column: "RiotAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_persona_account_conflicts_Status_LastSeenAtUtc",
                table: "persona_account_conflicts",
                columns: new[] { "Status", "LastSeenAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_persona_account_exclusions_ConflictId",
                table: "persona_account_exclusions",
                column: "ConflictId");

            migrationBuilder.CreateIndex(
                name: "IX_persona_account_exclusions_PersonaId_RiotAccountId",
                table: "persona_account_exclusions",
                columns: new[] { "PersonaId", "RiotAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_persona_account_exclusions_RiotAccountId",
                table: "persona_account_exclusions",
                column: "RiotAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_personas_personas_MergedIntoPersonaId",
                table: "personas",
                column: "MergedIntoPersonaId",
                principalTable: "personas",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Riot IDs compare case-insensitively: expressions the EF model cannot declare, as for
            // IX_persona_accounts_platform_riot_id_lower (AddPersonas).
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_persona_account_conflicts_claimant_riot_id_lower\" " +
                "ON persona_account_conflicts (\"ClaimantPersonaId\", \"PlatformId\", lower(\"GameName\"), lower(\"TagLine\"));");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_persona_account_exclusions_persona_riot_id_lower\" " +
                "ON persona_account_exclusions (\"PersonaId\", \"PlatformId\", lower(\"GameName\"), lower(\"TagLine\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_personas_personas_MergedIntoPersonaId",
                table: "personas");

            migrationBuilder.DropTable(
                name: "persona_account_exclusions");

            migrationBuilder.DropTable(
                name: "persona_account_conflicts");

            migrationBuilder.DropIndex(
                name: "IX_personas_MergedIntoPersonaId",
                table: "personas");

            migrationBuilder.DropColumn(
                name: "MergedIntoPersonaId",
                table: "personas");
        }
    }
}
