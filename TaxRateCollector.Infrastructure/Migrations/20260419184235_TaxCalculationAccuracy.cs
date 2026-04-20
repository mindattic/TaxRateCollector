using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaxCalculationAccuracy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdjustmentFrequency",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Static");

            migrationBuilder.AddColumn<string>(
                name: "AdjustmentMechanism",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CountsTowardLocalCap",
                table: "TaxRates",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FlatCapPerUnit",
                table: "TaxRates",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRecurring",
                table: "TaxRates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MinTaxableAmount",
                table: "TaxRates",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IntrastateSourcingRule",
                table: "StateTaxProfiles",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "DestinationBased");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdjustmentFrequency",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "AdjustmentMechanism",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "CountsTowardLocalCap",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "FlatCapPerUnit",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "IsRecurring",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "MinTaxableAmount",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "IntrastateSourcingRule",
                table: "StateTaxProfiles");
        }
    }
}
