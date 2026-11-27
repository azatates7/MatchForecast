using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatchForecast.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMatchResultYellowCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "away_yellow_cards",
                schema: "matchforecast",
                table: "match_results",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "home_yellow_cards",
                schema: "matchforecast",
                table: "match_results",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "away_yellow_cards",
                schema: "matchforecast",
                table: "match_results");

            migrationBuilder.DropColumn(
                name: "home_yellow_cards",
                schema: "matchforecast",
                table: "match_results");
        }
    }
}
