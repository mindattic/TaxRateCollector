using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceConfidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceConfidence",
                table: "TaxRates",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceConfidence",
                table: "TaxRates");
        }
    }
}
