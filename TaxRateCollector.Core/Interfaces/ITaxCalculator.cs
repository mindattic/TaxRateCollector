using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Core.Interfaces;

public interface ITaxCalculator
{
    Task<TaxCalcResult?> CalculateAsync(
        int jurisdictionId,
        decimal price,
        int quantity,
        TaxCalcOptions? options = null);
}

/// <summary>
/// Optional parameters that narrow which rate laws apply and supply product-specific
/// context needed to calculate non-percentage excise taxes.
/// </summary>
public record TaxCalcOptions(
    int? TaxCategoryId = null,
    /// <summary>Wholesale/manufacturer price per unit for PercentageOfWholesale rates.</summary>
    decimal? WholesalePrice = null,
    /// <summary>Volume in gallons per unit (FlatPerVolume / FlatPerProofGallon).</summary>
    decimal? Volume = null,
    /// <summary>Weight in ounces per unit (FlatPerWeight).</summary>
    decimal? Weight = null,
    /// <summary>ABV as a decimal fraction (0.05 = 5%). Filters MinAbv/MaxAbv rate laws.</summary>
    decimal? Abv = null,
    SaleContext SaleContext = SaleContext.Any);

/// <summary>Full tax breakdown for a jurisdiction + price + quantity calculation.</summary>
public record TaxCalcResult(
    /// <summary>Tax owed by the retailer / added to the customer invoice.</summary>
    decimal TotalTaxAmount,
    /// <summary>Upstream excise taxes already embedded in the product price (IsIncludedInPrice). Informational only — do NOT add to customer invoice.</summary>
    decimal TotalIncludedInPriceAmount,
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
    DateOnly? EffectiveDate,
    bool IsIncludedInPrice,
    bool IsCompound);
