namespace TaxRateCollector.Core.Interfaces;

public interface ITaxCalculator
{
    Task<TaxCalcResult?> CalculateAsync(int jurisdictionId, decimal price, int quantity);
}

public record TaxCalcResult(
    decimal TaxAmount,
    decimal Rate,
    string RateType,
    string EffectiveDate,
    string JurisdictionName);
