using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScrapeRunPauseResume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LastProcessedJurisdictionId",
                table: "ScrapeRuns",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastProcessedJurisdictionId",
                table: "ScrapeRuns");
        }
    }
}
