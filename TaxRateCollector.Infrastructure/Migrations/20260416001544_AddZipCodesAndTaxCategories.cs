using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddZipCodesAndTaxCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaxCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ParentId = table.Column<int>(type: "INTEGER", nullable: true),
                    TopLevelType = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    IsLeaf = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxCategories_TaxCategories_ParentId",
                        column: x => x.ParentId,
                        principalTable: "TaxCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ZipCodes",
                columns: table => new
                {
                    ZipCode = table.Column<string>(type: "TEXT", nullable: false),
                    StateCode = table.Column<string>(type: "TEXT", nullable: false),
                    StateFips = table.Column<string>(type: "TEXT", nullable: false),
                    CountyFips = table.Column<string>(type: "TEXT", nullable: false),
                    CountyName = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryCity = table.Column<string>(type: "TEXT", nullable: false),
                    StateJurisdictionId = table.Column<int>(type: "INTEGER", nullable: true),
                    CountyJurisdictionId = table.Column<int>(type: "INTEGER", nullable: true),
                    CityJurisdictionId = table.Column<int>(type: "INTEGER", nullable: true),
                    ImportedAt = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ZipCodes", x => x.ZipCode);
                });

            migrationBuilder.CreateTable(
                name: "TaxCategoryRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaxCategoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    JurisdictionId = table.Column<int>(type: "INTEGER", nullable: false),
                    IsExempt = table.Column<bool>(type: "INTEGER", nullable: false),
                    OverrideRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveDate = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxCategoryRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxCategoryRules_Jurisdictions_JurisdictionId",
                        column: x => x.JurisdictionId,
                        principalTable: "Jurisdictions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaxCategoryRules_TaxCategories_TaxCategoryId",
                        column: x => x.TaxCategoryId,
                        principalTable: "TaxCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaxCategories_ParentId",
                table: "TaxCategories",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxCategories_TopLevelType",
                table: "TaxCategories",
                column: "TopLevelType");

            migrationBuilder.CreateIndex(
                name: "IX_TaxCategoryRules_JurisdictionId",
                table: "TaxCategoryRules",
                column: "JurisdictionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxCategoryRules_TaxCategoryId_JurisdictionId",
                table: "TaxCategoryRules",
                columns: new[] { "TaxCategoryId", "JurisdictionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ZipCodes_CountyFips",
                table: "ZipCodes",
                column: "CountyFips");

            migrationBuilder.CreateIndex(
                name: "IX_ZipCodes_StateCode",
                table: "ZipCodes",
                column: "StateCode");

            migrationBuilder.CreateIndex(
                name: "IX_ZipCodes_StateFips",
                table: "ZipCodes",
                column: "StateFips");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaxCategoryRules");

            migrationBuilder.DropTable(
                name: "ZipCodes");

            migrationBuilder.DropTable(
                name: "TaxCategories");
        }
    }
}
