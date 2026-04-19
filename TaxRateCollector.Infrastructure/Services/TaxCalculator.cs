using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

public class TaxCalculator(IDbContextFactory<AppDbContext> dbFactory) : ITaxCalculator
{
    public async Task<TaxCalcResult?> CalculateAsync(
        int jurisdictionId, decimal price, int quantity, int? taxCategoryId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var jur = await db.Jurisdictions.FindAsync(jurisdictionId);
        if (jur is null) return null;

        var rates = await db.TaxRates
            .Where(t => t.JurisdictionId == jurisdictionId
                     && t.IsCurrent
                     && (taxCategoryId == null || t.TaxCategoryId == taxCategoryId))
            .OrderBy(t => t.Name)
            .ToListAsync();

        if (rates.Count == 0) return null;

        var subtotal = price * quantity;
        var lines = rates.Select(r =>
        {
            var taxAmount = r.RateBasis == RateBasis.Percentage
                ? subtotal * r.Rate
                : r.Rate * quantity;
            return new TaxRateLine(r.Name, r.Rate, r.RateBasis, r.Unit, r.SaleContext,
                r.RemittancePoint, taxAmount, r.EffectiveDate);
        }).ToList();

        var totalPct = rates
            .Where(r => r.RateBasis == RateBasis.Percentage)
            .Sum(r => r.Rate);

        return new TaxCalcResult(
            TotalTaxAmount: lines.Sum(l => l.TaxAmount),
            TotalPercentageRate: totalPct,
            RateLines: lines,
            JurisdictionName: jur.JurisdictionName);
    }
}
