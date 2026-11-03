using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SoftRejectPayPalSelectionAndWiNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRejected",
                table: "TaxRates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SelectedCategoryIds",
                table: "BillingRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SelectedStateCodes",
                table: "BillingRecords",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // Wisconsin bulk-scraper rate names were stored with a literal HTML entity
            // ("Sales &amp; Use Tax"). The scraper now emits "&"; fix existing rows so the
            // name match against the next scrape still finds them.
            migrationBuilder.Sql("UPDATE TaxRates SET Name = REPLACE(Name, '&amp;', '&') WHERE Name LIKE '%&amp;%';");
            migrationBuilder.Sql("UPDATE ChangeLog SET RateName = REPLACE(RateName, '&amp;', '&') WHERE RateName LIKE '%&amp;%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRejected",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "SelectedCategoryIds",
                table: "BillingRecords");

            migrationBuilder.DropColumn(
                name: "SelectedStateCodes",
                table: "BillingRecords");
        }
    }
}
