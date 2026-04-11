using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

public class TaxCalculator(IDbContextFactory<AppDbContext> dbFactory) : ITaxCalculator
{
    public async Task<TaxCalcResult?> CalculateAsync(int jurisdictionId, decimal price, int quantity)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var rate = await db.TaxRates
            .Include(t => t.Jurisdiction)
            .Where(t => t.JurisdictionId == jurisdictionId && t.IsCurrent && t.RateType == "General")
            .OrderByDescending(t => t.EffectiveDate)
            .FirstOrDefaultAsync();

        if (rate is null) return null;

        return new TaxCalcResult(
            TaxAmount: price * quantity * rate.Rate,
            Rate: rate.Rate,
            RateType: rate.RateType,
            EffectiveDate: rate.EffectiveDate,
            JurisdictionName: rate.Jurisdiction.JurisdictionName);
    }
}
