using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <summary>
    /// Known players (#2016): a persona, its public links and the Riot accounts sources list for it.
    /// </summary>
    /// <remarks>
    /// The case-insensitive uniqueness of a Riot ID across personas is a functional index the EF
    /// model cannot declare (same as <c>AddRiotAccountLowerNameTagIndex</c>), so it is raw SQL here
    /// and absent from the model. The tables are new and empty, so plain DDL is instant.
    /// </remarks>
    public partial class AddPersonas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "personas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_personas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "persona_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TagLine = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    PlatformId = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    RiotAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persona_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_persona_accounts_personas_PersonaId",
                        column: x => x.PersonaId,
                        principalTable: "personas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_persona_accounts_riot_accounts_RiotAccountId",
                        column: x => x.RiotAccountId,
                        principalTable: "riot_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "persona_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonaId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_persona_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_persona_links_personas_PersonaId",
                        column: x => x.PersonaId,
                        principalTable: "personas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_persona_accounts_PersonaId",
                table: "persona_accounts",
                column: "PersonaId");

            migrationBuilder.CreateIndex(
                name: "IX_persona_accounts_RiotAccountId",
                table: "persona_accounts",
                column: "RiotAccountId",
                unique: true,
                filter: "\"RiotAccountId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_persona_links_PersonaId_Url",
                table: "persona_links",
                columns: new[] { "PersonaId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_personas_Slug",
                table: "personas",
                column: "Slug",
                unique: true);

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_persona_accounts_platform_riot_id_lower\" " +
                "ON persona_accounts (\"PlatformId\", lower(\"GameName\"), lower(\"TagLine\"));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "persona_accounts");

            migrationBuilder.DropTable(
                name: "persona_links");

            migrationBuilder.DropTable(
                name: "personas");
        }
    }
}
