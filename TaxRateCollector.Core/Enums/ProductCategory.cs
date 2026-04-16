namespace TaxRateCollector.Core.Enums;

/// <summary>
/// Product categories that attract excise ("sin") taxes beyond the general sales tax.
/// Rates vary by tier — federal, state, county, and city may each levy a separate excise.
/// </summary>
public enum ProductCategory
{
    Alcohol,
    Beer,
    Wine,
    Spirits,
    Tobacco,
    Cigarettes,
    Cannabis,
    Sugar,           // sugar-sweetened beverage taxes
    SoftDrinks,
    Firearms,
    Ammunition,
    Fuel,
    Hotel,           // lodging / hotel occupancy tax
    RentalCar,
    Lottery,
    Gaming
}
