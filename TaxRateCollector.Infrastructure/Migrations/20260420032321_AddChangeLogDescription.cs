using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChangeLogDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeDescription",
                table: "ChangeLog",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeDescription",
                table: "ChangeLog");
        }
    }
}
