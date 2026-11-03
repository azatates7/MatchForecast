using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatchForecast.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserFavoriteLeagues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_favorite_leagues",
                schema: "matchforecast",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "integer", nullable: false),
                    league = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_favorite_leagues", x => new { x.user_id, x.league });
                    table.ForeignKey(
                        name: "FK_user_favorite_leagues_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "matchforecast",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_favorite_leagues",
                schema: "matchforecast");
        }
    }
}
