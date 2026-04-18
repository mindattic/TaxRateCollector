using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPerCategoryRateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaxRates_JurisdictionId",
                table: "TaxRates");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRates_JurisdictionId_TaxCategoryId_Current",
                table: "TaxRates",
                columns: new[] { "JurisdictionId", "TaxCategoryId" },
                unique: true,
                filter: "[IsCurrent] = 1 AND [TaxCategoryId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaxRates_JurisdictionId_TaxCategoryId_Current",
                table: "TaxRates");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRates_JurisdictionId",
                table: "TaxRates",
                column: "JurisdictionId");
        }
    }
}
