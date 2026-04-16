using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SourceDocumentOneToMany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add FileName — stores the on-disk filename for file-based evidence
            migrationBuilder.AddColumn<string>(
                name: "FileName",
                table: "SourceDocuments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // Add IsActive — soft-delete; disassociate without destroying history
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "SourceDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // Drop unique index — one TaxRate can now have many SourceDocuments
            migrationBuilder.DropIndex(
                name: "IX_SourceDocuments_TaxRateId",
                table: "SourceDocuments");

            // Recreate as non-unique for query performance
            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_TaxRateId",
                table: "SourceDocuments",
                column: "TaxRateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SourceDocuments_TaxRateId",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(name: "FileName", table: "SourceDocuments");
            migrationBuilder.DropColumn(name: "IsActive", table: "SourceDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_TaxRateId",
                table: "SourceDocuments",
                column: "TaxRateId",
                unique: true);
        }
    }
}
