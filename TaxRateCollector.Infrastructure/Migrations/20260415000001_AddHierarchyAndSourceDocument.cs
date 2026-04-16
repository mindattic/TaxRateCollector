using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHierarchyAndSourceDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add self-referential ParentId to Jurisdictions for Country→State→County→City hierarchy.
            migrationBuilder.AddColumn<int>(
                name: "ParentId",
                table: "Jurisdictions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jurisdictions_ParentId",
                table: "Jurisdictions",
                column: "ParentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Jurisdictions_Jurisdictions_ParentId",
                table: "Jurisdictions",
                column: "ParentId",
                principalTable: "Jurisdictions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // One-to-one source document per TaxRate row — proves the rate's veracity.
            migrationBuilder.CreateTable(
                name: "SourceDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaxRateId = table.Column<int>(type: "INTEGER", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    RawContent = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceDocuments_TaxRates_TaxRateId",
                        column: x => x.TaxRateId,
                        principalTable: "TaxRates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_TaxRateId",
                table: "SourceDocuments",
                column: "TaxRateId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "SourceDocuments");

            migrationBuilder.DropForeignKey(
                name: "FK_Jurisdictions_Jurisdictions_ParentId",
                table: "Jurisdictions");

            migrationBuilder.DropIndex(
                name: "IX_Jurisdictions_ParentId",
                table: "Jurisdictions");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "Jurisdictions");
        }
    }
}
