using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxRateCollector.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MarkCombinedStrategyRates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCombinedRate",
                table: "TaxRates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // The CA-CSV / IL-TABLE / TX-EXCEL statewide strategies store each locality's
            // published COMBINED rate (state + local). Rows they already wrote (Name "General",
            // RawEvidence set, a local jurisdiction in CA/IL/TX) are marked so the TRC-LAW-6
            // chain sum doesn't add the state/county rates on top of them again.
            migrationBuilder.Sql(@"
UPDATE t SET t.IsCombinedRate = 1
FROM TaxRates t
JOIN Jurisdictions j ON j.Id = t.JurisdictionId
WHERE j.StateCode IN ('CA', 'IL', 'TX')
  AND j.JurisdictionType <> 'State'
  AND t.Name = 'General'
  AND t.RawEvidence <> ''
  AND t.RateBasis = 'Percentage';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCombinedRate",
                table: "TaxRates");
        }
    }
}
