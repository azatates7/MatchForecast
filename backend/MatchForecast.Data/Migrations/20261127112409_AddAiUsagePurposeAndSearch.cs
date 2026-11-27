using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatchForecast.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsagePurposeAndSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "purpose",
                schema: "matchforecast",
                table: "ai_usages",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Forecast");

            migrationBuilder.AddColumn<int>(
                name: "search_queries",
                schema: "matchforecast",
                table: "ai_usages",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "matchforecast",
                table: "ai_usages");

            migrationBuilder.DropColumn(
                name: "search_queries",
                schema: "matchforecast",
                table: "ai_usages");
        }
    }
}
