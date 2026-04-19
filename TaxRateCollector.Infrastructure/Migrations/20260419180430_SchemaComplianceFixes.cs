using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SchemaComplianceFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Pre-conversion: replace empty/invalid date strings with a sentinel ──
            // The existing columns are NOT NULL (nvarchar), so we can't set NULL directly.
            // Replace any empty or non-parseable value with '1900-01-01' as a sentinel,
            // then null those rows out after the column becomes nullable date.
            migrationBuilder.Sql("UPDATE TaxRates           SET EffectiveDate  = '1900-01-01' WHERE ISNULL(EffectiveDate,  '') = '' OR ISDATE(EffectiveDate)  = 0");
            migrationBuilder.Sql("UPDATE TaxRates           SET ExpirationDate = '1900-01-01' WHERE ISNULL(ExpirationDate, '') = '' OR ISDATE(ExpirationDate) = 0");
            migrationBuilder.Sql("UPDATE TaxCategoryRules   SET EffectiveDate  = '1900-01-01' WHERE ISNULL(EffectiveDate,  '') = '' OR ISDATE(EffectiveDate)  = 0");
            migrationBuilder.Sql("UPDATE StateCategoryRules SET EffectiveDate  = '1900-01-01' WHERE ISNULL(EffectiveDate,  '') = '' OR ISDATE(EffectiveDate)  = 0");

            // ── TaxRates: rename RawValue → RawEvidence ───────────────────────
            migrationBuilder.RenameColumn(
                name: "RawValue",
                table: "TaxRates",
                newName: "RawEvidence");

            // ── TaxRates: new compliance columns ──────────────────────────────
            migrationBuilder.AddColumn<string>(
                name: "TaxType",
                table: "TaxRates",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "SalesTax");

            migrationBuilder.AddColumn<bool>(
                name: "IsIncludedInPrice",
                table: "TaxRates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ProductCategory",
                table: "TaxRates",
                type: "nvarchar(450)",
                nullable: true);

            // ── TaxRates: date type changes ────────────────────────────────────
            migrationBuilder.AlterColumn<DateOnly>(
                name: "ExpirationDate",
                table: "TaxRates",
                type: "date",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EffectiveDate",
                table: "TaxRates",
                type: "date",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            // ── TaxCategoryRules: add Taxability, migrate IsExempt, then separate ─
            migrationBuilder.AddColumn<string>(
                name: "Taxability",
                table: "TaxCategoryRules",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "Taxable");

            // Preserve IsExempt = true rows as Exempt before the column is dropped
            migrationBuilder.Sql("UPDATE TaxCategoryRules SET Taxability = 'Exempt' WHERE IsExempt = 1");

            // IsExempt is replaced by the Taxability enum — they are different concepts.
            // IsExempt was a bool; Taxability is Taxable/Exempt/ReducedRate/SpecialRule.
            migrationBuilder.DropColumn(
                name: "IsExempt",
                table: "TaxCategoryRules");

            // LocalRateApplies is a new field with no equivalent in the old schema.
            migrationBuilder.AddColumn<bool>(
                name: "LocalRateApplies",
                table: "TaxCategoryRules",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUrl",
                table: "TaxCategoryRules",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatutoryReference",
                table: "TaxCategoryRules",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "TaxCategoryRules",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<DateOnly>(
                name: "EffectiveDate",
                table: "TaxCategoryRules",
                type: "date",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            // ── StateCategoryRules: date type change ───────────────────────────
            migrationBuilder.AlterColumn<DateOnly>(
                name: "EffectiveDate",
                table: "StateCategoryRules",
                type: "date",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            // ── Post-conversion: null out sentinel dates (column is now nullable) ─
            migrationBuilder.Sql("UPDATE TaxRates           SET EffectiveDate  = NULL WHERE EffectiveDate  = '1900-01-01'");
            migrationBuilder.Sql("UPDATE TaxRates           SET ExpirationDate = NULL WHERE ExpirationDate = '1900-01-01'");
            migrationBuilder.Sql("UPDATE TaxCategoryRules   SET EffectiveDate  = NULL WHERE EffectiveDate  = '1900-01-01'");
            migrationBuilder.Sql("UPDATE StateCategoryRules SET EffectiveDate  = NULL WHERE EffectiveDate  = '1900-01-01'");

            // ── Index: fast excise rate lookups by product category ────────────
            migrationBuilder.CreateIndex(
                name: "IX_TaxRates_JurisdictionId_ProductCategory_TaxType_Current",
                table: "TaxRates",
                columns: new[] { "JurisdictionId", "ProductCategory", "TaxType", "IsCurrent" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaxRates_JurisdictionId_ProductCategory_TaxType_Current",
                table: "TaxRates");

            migrationBuilder.DropColumn(name: "IsIncludedInPrice",   table: "TaxRates");
            migrationBuilder.DropColumn(name: "ProductCategory",     table: "TaxRates");
            migrationBuilder.DropColumn(name: "TaxType",             table: "TaxRates");
            migrationBuilder.DropColumn(name: "LocalRateApplies",    table: "TaxCategoryRules");
            migrationBuilder.DropColumn(name: "SourceUrl",           table: "TaxCategoryRules");
            migrationBuilder.DropColumn(name: "StatutoryReference",  table: "TaxCategoryRules");
            migrationBuilder.DropColumn(name: "Taxability",          table: "TaxCategoryRules");

            migrationBuilder.RenameColumn(
                name: "RawEvidence",
                table: "TaxRates",
                newName: "RawValue");

            // Restore IsExempt from Taxability before dropping Taxability
            migrationBuilder.AddColumn<bool>(
                name: "IsExempt",
                table: "TaxCategoryRules",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "ExpirationDate",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EffectiveDate",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Notes",
                table: "TaxCategoryRules",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EffectiveDate",
                table: "TaxCategoryRules",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "EffectiveDate",
                table: "StateCategoryRules",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);
        }
    }
}
