using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExciseTaxRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExciseTaxRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JurisdictionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductCategory = table.Column<string>(type: "TEXT", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: false),
                    RateType = table.Column<string>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveDate = table.Column<string>(type: "TEXT", nullable: false),
                    ScrapedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ScrapeRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawValue = table.Column<string>(type: "TEXT", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ExciseTaxRateId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    RawContent = table.Column<string>(type: "TEXT", nullable: false)
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
                name: "IX_ExciseTaxRates_JurisdictionId",
                table: "ExciseTaxRates",
                column: "JurisdictionId");

            migrationBuilder.CreateIndex(
                name: "IX_ExciseTaxRates_ScrapeRunId",
                table: "ExciseTaxRates",
                column: "ScrapeRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ExciseSourceDocuments_ExciseTaxRateId",
                table: "ExciseSourceDocuments",
                column: "ExciseTaxRateId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ExciseSourceDocuments");
            migrationBuilder.DropTable(name: "ExciseTaxRates");
        }
    }
}
