using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ZokaAnalytics.Migrations
{
    /// <inheritdoc />
    public partial class MatchContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MatchContexts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MatchId = table.Column<int>(type: "integer", nullable: false),
                    HomeKeyPlayersMissing = table.Column<int>(type: "integer", nullable: false),
                    AwayKeyPlayersMissing = table.Column<int>(type: "integer", nullable: false),
                    HomeCoachChanged = table.Column<bool>(type: "boolean", nullable: false),
                    AwayCoachChanged = table.Column<bool>(type: "boolean", nullable: false),
                    IsDerby = table.Column<bool>(type: "boolean", nullable: false),
                    HomeNothingToPlayFor = table.Column<bool>(type: "boolean", nullable: false),
                    AwayNothingToPlayFor = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MatchContexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MatchContexts_Matches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "Matches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MatchContexts_MatchId",
                table: "MatchContexts",
                column: "MatchId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MatchContexts");
        }
    }
}
