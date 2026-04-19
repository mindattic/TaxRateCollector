using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorTaxRateLaws : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExciseSourceDocuments");

            migrationBuilder.DropTable(
                name: "ExciseTaxRates");

            migrationBuilder.DropIndex(
                name: "IX_TaxRates_JurisdictionId_TaxCategoryId_Current",
                table: "TaxRates");

            migrationBuilder.RenameColumn(
                name: "RateType",
                table: "TaxRates",
                newName: "Unit");

            migrationBuilder.AlterColumn<decimal>(
                name: "Rate",
                table: "TaxRates",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<string>(
                name: "Conditions",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExpirationDate",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MaxAbv",
                table: "TaxRates",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MinAbv",
                table: "TaxRates",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RateBasis",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RemittancePoint",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SaleContext",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StatutoryReference",
                table: "TaxRates",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<decimal>(
                name: "OverrideRate",
                table: "TaxCategoryRules",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "LocalRateCap",
                table: "StateTaxProfiles",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "GeneralSalesTaxRate",
                table: "StateTaxProfiles",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "StateRate",
                table: "StateCategoryRules",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "OldRate",
                table: "ChangeLog",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "NewRate",
                table: "ChangeLog",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RateName",
                table: "ChangeLog",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TaxRateId",
                table: "ChangeLog",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ZipCodeDistricts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ZipCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    JurisdictionId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZipCodeDistricts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ZipCodeDistricts_Jurisdictions_JurisdictionId",
                        column: x => x.JurisdictionId,
                        principalTable: "Jurisdictions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ZipCodeDistricts_ZipCodes_ZipCode",
                        column: x => x.ZipCode,
                        principalTable: "ZipCodes",
                        principalColumn: "ZipCode",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRates_JurisdictionId_CategoryId_Current",
                table: "TaxRates",
                columns: new[] { "JurisdictionId", "TaxCategoryId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeLog_TaxRateId",
                table: "ChangeLog",
                column: "TaxRateId");

            migrationBuilder.CreateIndex(
                name: "IX_ZipCodeDistricts_JurisdictionId",
                table: "ZipCodeDistricts",
                column: "JurisdictionId");

            migrationBuilder.CreateIndex(
                name: "IX_ZipCodeDistricts_ZipCode_JurisdictionId",
                table: "ZipCodeDistricts",
                columns: new[] { "ZipCode", "JurisdictionId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChangeLog_TaxRates_TaxRateId",
                table: "ChangeLog",
                column: "TaxRateId",
                principalTable: "TaxRates",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChangeLog_TaxRates_TaxRateId",
                table: "ChangeLog");

            migrationBuilder.DropTable(
                name: "ZipCodeDistricts");

            migrationBuilder.DropIndex(
                name: "IX_TaxRates_JurisdictionId_CategoryId_Current",
                table: "TaxRates");

            migrationBuilder.DropIndex(
                name: "IX_ChangeLog_TaxRateId",
                table: "ChangeLog");

            migrationBuilder.DropColumn(
                name: "Conditions",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "MaxAbv",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "MinAbv",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "RateBasis",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "RemittancePoint",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "SaleContext",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "StatutoryReference",
                table: "TaxRates");

            migrationBuilder.DropColumn(
                name: "RateName",
                table: "ChangeLog");

            migrationBuilder.DropColumn(
                name: "TaxRateId",
                table: "ChangeLog");

            migrationBuilder.RenameColumn(
                name: "Unit",
                table: "TaxRates",
                newName: "RateType");

            migrationBuilder.AlterColumn<decimal>(
                name: "Rate",
                table: "TaxRates",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<decimal>(
                name: "OverrideRate",
                table: "TaxCategoryRules",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "LocalRateCap",
                table: "StateTaxProfiles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "GeneralSalesTaxRate",
                table: "StateTaxProfiles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<decimal>(
                name: "StateRate",
                table: "StateCategoryRules",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "OldRate",
                table: "ChangeLog",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "NewRate",
                table: "ChangeLog",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ExciseTaxRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JurisdictionId = table.Column<int>(type: "int", nullable: false),
                    ScrapeRunId = table.Column<int>(type: "int", nullable: false),
                    EffectiveDate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    ProductCategory = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RateType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScrapedAt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExciseTaxRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExciseTaxRates_Jurisdictions_JurisdictionId",
                        column: x => x.JurisdictionId,
                        principalTable: "Jurisdictions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExciseTaxRates_ScrapeRuns_ScrapeRunId",
                        column: x => x.ScrapeRunId,
                        principalTable: "ScrapeRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExciseSourceDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExciseTaxRateId = table.Column<int>(type: "int", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FetchedAt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MimeType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RawContent = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExciseSourceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExciseSourceDocuments_ExciseTaxRates_ExciseTaxRateId",
                        column: x => x.ExciseTaxRateId,
                        principalTable: "ExciseTaxRates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxRates_JurisdictionId_TaxCategoryId_Current",
                table: "TaxRates",
                columns: new[] { "JurisdictionId", "TaxCategoryId" },
                unique: true,
                filter: "[IsCurrent] = 1 AND [TaxCategoryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExciseSourceDocuments_ExciseTaxRateId",
                table: "ExciseSourceDocuments",
                column: "ExciseTaxRateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExciseTaxRates_JurisdictionId",
                table: "ExciseTaxRates",
                column: "JurisdictionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExciseTaxRates_ScrapeRunId",
                table: "ExciseTaxRates",
                column: "ScrapeRunId");
        }
    }
}
