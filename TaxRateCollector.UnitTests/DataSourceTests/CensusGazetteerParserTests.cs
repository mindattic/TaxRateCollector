using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.DataSourceTests;

[TestFixture]
public class CensusGazetteerParserTests
{
    // ── Shared state code map used across all tests ───────────────────────────

    private static readonly IReadOnlyDictionary<string, string> FipsMap =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["01"] = "AL", ["06"] = "CA", ["17"] = "IL",
            ["36"] = "NY", ["48"] = "TX",
        };

    // ── ExtractFips ───────────────────────────────────────────────────────────

    [Test]
    public void ExtractFips_BareCountyFips_ReturnedUnchanged()
        => Assert.That(CensusGazetteerParser.ExtractFips("01001", 5), Is.EqualTo("01001"));

    [Test]
    public void ExtractFips_SummaryLevelPrefix_Stripped()
        => Assert.That(CensusGazetteerParser.ExtractFips("0500000US01001", 5), Is.EqualTo("01001"));

    [Test]
    public void ExtractFips_PlaceFipsWithPrefix_Stripped()
        => Assert.That(CensusGazetteerParser.ExtractFips("1600000US0100100", 7), Is.EqualTo("0100100"));

    [Test]
    public void ExtractFips_TooFewDigits_ReturnsNull()
        => Assert.That(CensusGazetteerParser.ExtractFips("0123", 5), Is.Null);

    [Test]
    public void ExtractFips_NullInput_ReturnsNull()
        => Assert.That(CensusGazetteerParser.ExtractFips(null!, 5), Is.Null);

    [Test]
    public void ExtractFips_EmptyInput_ReturnsNull()
        => Assert.That(CensusGazetteerParser.ExtractFips("", 5), Is.Null);

    [Test]
    public void ExtractFips_NoDigits_ReturnsNull()
        => Assert.That(CensusGazetteerParser.ExtractFips("abc", 5), Is.Null);

    [Test]
    public void ExtractFips_ExactLength_ReturnedAsIs()
        => Assert.That(CensusGazetteerParser.ExtractFips("36061", 5), Is.EqualTo("36061"));

    // ── ParseGazetteerCounties ────────────────────────────────────────────────

    private static string CountyFile(char delim, params string[] dataRows)
    {
        var header = $"USPS{delim}GEOID{delim}ANSICODE{delim}NAME{delim}ALAND";
        return string.Join("\n", new[] { header }.Concat(dataRows));
    }

    [Test]
    public void ParseGazetteerCounties_PipeDelimited_Parsed()
    {
        var content = CountyFile('|', "AL|01001|foo|Autauga County|12345678");
        var result  = CensusGazetteerParser.ParseGazetteerCounties(content, FipsMap);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Fips, Is.EqualTo("01001"));
        Assert.That(result[0].Name, Is.EqualTo("Autauga County"));
        Assert.That(result[0].StateCode, Is.EqualTo("AL"));
        Assert.That(result[0].StateFips, Is.EqualTo("01"));
    }

    [Test]
    public void ParseGazetteerCounties_TabDelimited_Parsed()
    {
        var content = CountyFile('\t', "CA\t06037\tfoo\tLos Angeles County\t999");
        var result  = CensusGazetteerParser.ParseGazetteerCounties(content, FipsMap);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Fips, Is.EqualTo("06037"));
        Assert.That(result[0].Name, Is.EqualTo("Los Angeles County"));
    }

    [Test]
    public void ParseGazetteerCounties_ShortGeoid_PaddedToFiveDigits()
    {
        var content = CountyFile('|', "AL|1001|foo|Autauga County|123");
        var result  = CensusGazetteerParser.ParseGazetteerCounties(content, FipsMap);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Fips, Is.EqualTo("01001"),
            "Short GEOID should be left-padded to 5 digits");
    }

    [Test]
    public void ParseGazetteerCounties_EmptyName_RowSkipped()
    {
        var content = CountyFile('|', "AL|01001|foo||123");
        var result  = CensusGazetteerParser.ParseGazetteerCounties(content, FipsMap);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseGazetteerCounties_NoUspsColumn_StateCodeFallsBackToFipsMap()
    {
        var header = "GEOID|NAME";
        var content = header + "\n06037|Los Angeles County";
        var result  = CensusGazetteerParser.ParseGazetteerCounties(content, FipsMap);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].StateCode, Is.EqualTo("CA"),
            "Without USPS column, StateCode should be resolved from FipsToStateCode");
    }

    [Test]
    public void ParseGazetteerCounties_EmptyContent_ReturnsEmpty()
        => Assert.That(CensusGazetteerParser.ParseGazetteerCounties("", FipsMap), Is.Empty);

    [Test]
    public void ParseGazetteerCounties_HeaderOnly_ReturnsEmpty()
        => Assert.That(CensusGazetteerParser.ParseGazetteerCounties("USPS|GEOID|NAME", FipsMap), Is.Empty);

    [Test]
    public void ParseGazetteerCounties_MultipleRows_AllReturned()
    {
        var content = CountyFile('|',
            "AL|01001|x|Autauga County|1",
            "CA|06037|x|Los Angeles County|2");
        var result = CensusGazetteerParser.ParseGazetteerCounties(content, FipsMap);

        Assert.That(result, Has.Count.EqualTo(2));
    }

    // ── ParseGazetteerPlaces ──────────────────────────────────────────────────

    private static string PlaceFile(char delim, params string[] dataRows)
    {
        var header = $"USPS{delim}GEOID{delim}ANSICODE{delim}NAME{delim}ALAND";
        return string.Join("\n", new[] { header }.Concat(dataRows));
    }

    [Test]
    public void ParseGazetteerPlaces_PipeDelimited_Parsed()
    {
        var content = PlaceFile('|', "AL|0100100|foo|Abanda|99");
        var result  = CensusGazetteerParser.ParseGazetteerPlaces(content, FipsMap);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Fips, Is.EqualTo("0100100"));
        Assert.That(result[0].Name, Is.EqualTo("Abanda"));
        Assert.That(result[0].StateCode, Is.EqualTo("AL"));
        Assert.That(result[0].StateFips, Is.EqualTo("01"));
    }

    [Test]
    public void ParseGazetteerPlaces_ShortGeoid_PaddedToSevenDigits()
    {
        var content = PlaceFile('|', "AL|100100|foo|Abanda|99");
        var result  = CensusGazetteerParser.ParseGazetteerPlaces(content, FipsMap);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Fips, Is.EqualTo("0100100"));
    }

    [Test]
    public void ParseGazetteerPlaces_EmptyName_RowSkipped()
    {
        var content = PlaceFile('|', "AL|0100100|foo||99");
        Assert.That(CensusGazetteerParser.ParseGazetteerPlaces(content, FipsMap), Is.Empty);
    }

    [Test]
    public void ParseGazetteerPlaces_TabDelimited_Parsed()
    {
        var content = PlaceFile('\t', "CA\t0600100\tfoo\tAdelanto\t1");
        var result  = CensusGazetteerParser.ParseGazetteerPlaces(content, FipsMap);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Name, Is.EqualTo("Adelanto"));
    }

    // ── ParseZctaCountyMap ────────────────────────────────────────────────────

    private static string ZctaCountyFile(params string[] dataRows)
    {
        const string header = "OID|GEOID_ZCTA5_20|EXTRA|GEOID_COUNTY_20|AREALAND_PART";
        return string.Join("\n", new[] { header }.Concat(dataRows));
    }

    [Test]
    public void ParseZctaCountyMap_SingleRow_Parsed()
    {
        var content = ZctaCountyFile("1|90001|x|06037|5000000");
        var result  = CensusGazetteerParser.ParseZctaCountyMap(content);

        Assert.That(result, Does.ContainKey("90001"));
        Assert.That(result["90001"], Is.EqualTo("06037"));
    }

    [Test]
    public void ParseZctaCountyMap_MultipleRowsSameZcta_PicksLargestArea()
    {
        var content = ZctaCountyFile(
            "1|90001|x|06037|1000000",
            "2|90001|x|06059|9000000");
        var result = CensusGazetteerParser.ParseZctaCountyMap(content);

        Assert.That(result["90001"], Is.EqualTo("06059"));
    }

    [Test]
    public void ParseZctaCountyMap_EmptyContent_ReturnsEmpty()
        => Assert.That(CensusGazetteerParser.ParseZctaCountyMap(""), Is.Empty);

    // ── BuildPlaceCountyFromZcta ──────────────────────────────────────────────

    private static string ZctaPlaceFile(params string[] dataRows)
    {
        const string header = "OID|GEOID_ZCTA5_20|GEOID_PLACE_20|AREALAND_PART";
        return string.Join("\n", new[] { header }.Concat(dataRows));
    }

    [Test]
    public void BuildPlaceCountyFromZcta_MatchingZcta_ReturnsMappedCounty()
    {
        var content = ZctaPlaceFile("1|90001|0614000|5000000");
        var zctaMap = new Dictionary<string, string> { ["90001"] = "06037" };
        var result  = CensusGazetteerParser.BuildPlaceCountyFromZcta(content, zctaMap);

        Assert.That(result, Does.ContainKey("0614000"));
        Assert.That(result["0614000"], Is.EqualTo("06037"));
    }

    [Test]
    public void BuildPlaceCountyFromZcta_ZctaNotInMap_PlaceSkipped()
    {
        var content = ZctaPlaceFile("1|99999|0614000|5000000");
        var zctaMap = new Dictionary<string, string> { ["90001"] = "06037" };
        var result  = CensusGazetteerParser.BuildPlaceCountyFromZcta(content, zctaMap);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void BuildPlaceCountyFromZcta_MultipleZctasSamePlace_PicksLargestArea()
    {
        var content = ZctaPlaceFile(
            "1|90001|0614000|1000000",
            "2|90210|0614000|9000000");
        var zctaMap = new Dictionary<string, string>
        {
            ["90001"] = "06037",
            ["90210"] = "06059",
        };
        var result = CensusGazetteerParser.BuildPlaceCountyFromZcta(content, zctaMap);

        Assert.That(result["0614000"], Is.EqualTo("06059"),
            "Place should be mapped to county via highest-area ZCTA intersection");
    }

    [Test]
    public void BuildPlaceCountyFromZcta_EmptyContent_ReturnsEmpty()
        => Assert.That(CensusGazetteerParser.BuildPlaceCountyFromZcta("", new Dictionary<string, string>()), Is.Empty);

    // ── ParsePlaceCountyRel ───────────────────────────────────────────────────

    private static string PlaceCountyFile(params string[] dataRows)
    {
        const string header = "GEOID_PLC_20|GEOID_CNTY_20|NAME_PLC_20|AREALAND_INT";
        return string.Join("\n", new[] { header }.Concat(dataRows));
    }

    [Test]
    public void ParsePlaceCountyRel_SingleRow_Parsed()
    {
        var content = PlaceCountyFile("0614000|06037|Los Angeles|8000000");
        var result  = CensusGazetteerParser.ParsePlaceCountyRel(content);

        Assert.That(result, Does.ContainKey("0614000"));
        Assert.That(result["0614000"], Is.EqualTo("06037"));
    }

    [Test]
    public void ParsePlaceCountyRel_PrefixedGeoIds_Stripped()
    {
        var content = PlaceCountyFile("1600000US0614000|0500000US06037|Los Angeles|5000000");
        var result  = CensusGazetteerParser.ParsePlaceCountyRel(content);

        Assert.That(result, Does.ContainKey("0614000"));
        Assert.That(result["0614000"], Is.EqualTo("06037"));
    }

    [Test]
    public void ParsePlaceCountyRel_MultipleCountiesSamePlace_PicksLargestArea()
    {
        var content = PlaceCountyFile(
            "0614000|06037|Los Angeles|1000000",
            "0614000|06059|Orange|9000000");
        var result = CensusGazetteerParser.ParsePlaceCountyRel(content);

        Assert.That(result["0614000"], Is.EqualTo("06059"));
    }

    [Test]
    public void ParsePlaceCountyRel_TooShortPlaceFips_Skipped()
    {
        var content = PlaceCountyFile("0614|06037|LA|5000000");
        Assert.That(CensusGazetteerParser.ParsePlaceCountyRel(content), Is.Empty,
            "Place with fewer than 7 digits should be skipped");
    }

    [Test]
    public void ParsePlaceCountyRel_EmptyContent_ReturnsEmpty()
        => Assert.That(CensusGazetteerParser.ParsePlaceCountyRel(""), Is.Empty);
}
