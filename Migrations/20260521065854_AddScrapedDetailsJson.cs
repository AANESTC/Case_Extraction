using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ECourtTracker.API.Migrations
{
    /// <inheritdoc />
    public partial class AddScrapedDetailsJson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ScrapedDetailsJson",
                table: "Cases",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScrapedDetailsJson",
                table: "Cases");
        }
    }
}
