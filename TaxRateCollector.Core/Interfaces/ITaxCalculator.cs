using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Interfaces;

public interface ITaxCalculator
{
    Task<TaxCalcResult?> CalculateAsync(int jurisdictionId, decimal price, int quantity, int? taxCategoryId = null);
}

/// <summary>Full tax breakdown for a jurisdiction + price + quantity calculation.</summary>
public record TaxCalcResult(
    decimal TotalTaxAmount,
    decimal TotalPercentageRate,
    IReadOnlyList<TaxRateLine> RateLines,
    string JurisdictionName);

/// <summary>One named rate law's contribution to the total.</summary>
public record TaxRateLine(
    string RateName,
    decimal Rate,
    RateBasis Basis,
    string Unit,
    SaleContext SaleContext,
    RemittancePoint RemittancePoint,
    decimal TaxAmount,
    DateOnly? EffectiveDate);
