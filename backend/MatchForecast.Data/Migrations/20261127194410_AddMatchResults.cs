using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MatchForecast.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "match_results",
                schema: "matchforecast",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fixture_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    home_goals = table.Column<int>(type: "integer", nullable: true),
                    away_goals = table.Column<int>(type: "integer", nullable: true),
                    half_time_home_goals = table.Column<int>(type: "integer", nullable: true),
                    half_time_away_goals = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_results", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_match_results_fixture_id",
                schema: "matchforecast",
                table: "match_results",
                column: "fixture_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_results",
                schema: "matchforecast");
        }
    }
}
