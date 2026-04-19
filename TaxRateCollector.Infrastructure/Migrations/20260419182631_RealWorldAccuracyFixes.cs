using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RealWorldAccuracyFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCompound",
                table: "TaxRates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsTemporary",
                table: "TaxRates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxTaxableAmount",
                table: "TaxRates",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "EconomicNexusThresholdAmount",
                table: "StateTaxProfiles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EconomicNexusThresholdTransactions",
                table: "StateTaxProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHomeRuleAdministered",
                table: "Jurisdictions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCompound",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "IsTemporary",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "MaxTaxableAmount",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "EconomicNexusThresholdAmount",
                table: "StateTaxProfiles");

            migrationBuilder.DropColumn(
                name: "EconomicNexusThresholdTransactions",
                table: "StateTaxProfiles");

            migrationBuilder.DropColumn(
                name: "IsHomeRuleAdministered",
                table: "Jurisdictions");
        }
    }
}
