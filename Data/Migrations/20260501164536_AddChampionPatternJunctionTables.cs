using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChampionPatternJunctionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "champion_dim_builds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BootsItemId = table.Column<int>(type: "integer", nullable: false),
                    BuildItem0 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem1 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem2 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem3 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem4 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem5 = table.Column<int>(type: "integer", nullable: false),
                    BuildItem6 = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_dim_builds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_dim_rune_pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    StatDefense = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_dim_rune_pages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_dim_skill_orders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillOrderKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_dim_skill_orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_dim_spell_pairs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Spell1Id = table.Column<int>(type: "integer", nullable: false),
                    Spell2Id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_dim_spell_pairs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_dim_starter_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StarterItemsKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StarterItems = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_dim_starter_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "champion_aggregate_patterns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunePageId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpellPairId = table.Column<Guid>(type: "uuid", nullable: false),
                    StarterItemsId = table.Column<Guid>(type: "uuid", nullable: false),
                    Games = table.Column<int>(type: "integer", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_champion_aggregate_patterns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_patterns_champion_aggregate_scopes_Scope~",
                        column: x => x.ScopeId,
                        principalTable: "champion_aggregate_scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_patterns_champion_dim_builds_BuildId",
                        column: x => x.BuildId,
                        principalTable: "champion_dim_builds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_patterns_champion_dim_rune_pages_RunePag~",
                        column: x => x.RunePageId,
                        principalTable: "champion_dim_rune_pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_patterns_champion_dim_skill_orders_Skill~",
                        column: x => x.SkillOrderId,
                        principalTable: "champion_dim_skill_orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_patterns_champion_dim_spell_pairs_SpellP~",
                        column: x => x.SpellPairId,
                        principalTable: "champion_dim_spell_pairs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_champion_aggregate_patterns_champion_dim_starter_items_Star~",
                        column: x => x.StarterItemsId,
                        principalTable: "champion_dim_starter_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_BuildId",
                table: "champion_aggregate_patterns",
                column: "BuildId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_RunePageId",
                table: "champion_aggregate_patterns",
                column: "RunePageId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_ScopeId_BuildId",
                table: "champion_aggregate_patterns",
                columns: new[] { "ScopeId", "BuildId" });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_ScopeId_BuildId_RunePageId_Skil~",
                table: "champion_aggregate_patterns",
                columns: new[] { "ScopeId", "BuildId", "RunePageId", "SkillOrderId", "SpellPairId", "StarterItemsId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_ScopeId_RunePageId",
                table: "champion_aggregate_patterns",
                columns: new[] { "ScopeId", "RunePageId" });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_ScopeId_SkillOrderId",
                table: "champion_aggregate_patterns",
                columns: new[] { "ScopeId", "SkillOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_ScopeId_SpellPairId",
                table: "champion_aggregate_patterns",
                columns: new[] { "ScopeId", "SpellPairId" });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_ScopeId_StarterItemsId",
                table: "champion_aggregate_patterns",
                columns: new[] { "ScopeId", "StarterItemsId" });

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_SkillOrderId",
                table: "champion_aggregate_patterns",
                column: "SkillOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_SpellPairId",
                table: "champion_aggregate_patterns",
                column: "SpellPairId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_aggregate_patterns_StarterItemsId",
                table: "champion_aggregate_patterns",
                column: "StarterItemsId");

            migrationBuilder.CreateIndex(
                name: "IX_champion_dim_builds_BootsItemId_BuildItem0_BuildItem1_Build~",
                table: "champion_dim_builds",
                columns: new[] { "BootsItemId", "BuildItem0", "BuildItem1", "BuildItem2", "BuildItem3", "BuildItem4", "BuildItem5", "BuildItem6" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_dim_rune_pages_PrimaryStyleId_PrimaryKeystoneId_Pr~",
                table: "champion_dim_rune_pages",
                columns: new[] { "PrimaryStyleId", "PrimaryKeystoneId", "PrimaryPerk1Id", "PrimaryPerk2Id", "PrimaryPerk3Id", "SecondaryStyleId", "SecondaryPerk1Id", "SecondaryPerk2Id", "StatOffense", "StatFlex", "StatDefense" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_dim_skill_orders_SkillOrderKey",
                table: "champion_dim_skill_orders",
                column: "SkillOrderKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_dim_spell_pairs_Spell1Id_Spell2Id",
                table: "champion_dim_spell_pairs",
                columns: new[] { "Spell1Id", "Spell2Id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_champion_dim_starter_items_StarterItemsKey",
                table: "champion_dim_starter_items",
                column: "StarterItemsKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "champion_aggregate_patterns");

            migrationBuilder.DropTable(
                name: "champion_dim_builds");

            migrationBuilder.DropTable(
                name: "champion_dim_rune_pages");

            migrationBuilder.DropTable(
                name: "champion_dim_skill_orders");

            migrationBuilder.DropTable(
                name: "champion_dim_spell_pairs");

            migrationBuilder.DropTable(
                name: "champion_dim_starter_items");
        }
    }
}
