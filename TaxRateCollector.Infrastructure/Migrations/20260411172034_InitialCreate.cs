using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jurisdictions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StateCode = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionName = table.Column<string>(type: "TEXT", nullable: false),
                    JurisdictionType = table.Column<string>(type: "TEXT", nullable: false),
                    FipsCode = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jurisdictions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScrapeRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TotalScraped = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangesDetected = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapeRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChangeLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JurisdictionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", nullable: false),
                    OldRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    NewRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    DetectedAt = table.Column<string>(type: "TEXT", nullable: false),
                    Acknowledged = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeLog_Jurisdictions_JurisdictionId",
                        column: x => x.JurisdictionId,
                        principalTable: "Jurisdictions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaxRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JurisdictionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Rate = table.Column<decimal>(type: "TEXT", nullable: false),
                    RateType = table.Column<string>(type: "TEXT", nullable: false),
                    EffectiveDate = table.Column<string>(type: "TEXT", nullable: false),
                    ScrapedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ScrapeRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    RawValue = table.Column<string>(type: "TEXT", nullable: false),
                    IsCurrent = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaxRates_Jurisdictions_JurisdictionId",
                        column: x => x.JurisdictionId,
                        principalTable: "Jurisdictions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaxRates_ScrapeRuns_ScrapeRunId",
                        column: x => x.ScrapeRunId,
                        principalTable: "ScrapeRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeLog_JurisdictionId",
                table: "ChangeLog",
                column: "JurisdictionId");

            migrationBuilder.CreateIndex(
                name: "IX_Jurisdictions_FipsCode",
                table: "Jurisdictions",
                column: "FipsCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaxRates_JurisdictionId",
                table: "TaxRates",
                column: "JurisdictionId");

            migrationBuilder.CreateIndex(
                name: "IX_TaxRates_ScrapeRunId",
                table: "TaxRates",
                column: "ScrapeRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeLog");

            migrationBuilder.DropTable(
                name: "TaxRates");

            migrationBuilder.DropTable(
                name: "Jurisdictions");

            migrationBuilder.DropTable(
                name: "ScrapeRuns");
        }
    }
}
