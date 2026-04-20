using TaxRateCollector.Infrastructure.Services;

namespace TaxRateCollector.UnitTests.DataSourceTests;

[TestFixture]
public class ZipCrosswalkParserTests
{
    // ── ColIdx ────────────────────────────────────────────────────────────────

    [Test]
    public void ColIdx_FindsExactMatch()
    {
        var header = new[] { "OID", "GEOID_ZCTA5_20", "AREALAND_PART" };
        Assert.That(ZipCrosswalkParser.ColIdx(header, "GEOID_ZCTA5_20"), Is.EqualTo(1));
    }

    [Test]
    public void ColIdx_IsCaseInsensitive()
    {
        var header = new[] { "AREALAND_PART", "geoid_zcta5_20" };
        Assert.That(ZipCrosswalkParser.ColIdx(header, "GEOID_ZCTA5_20"), Is.EqualTo(1));
    }

    [Test]
    public void ColIdx_TrimsWhitespace()
    {
        var header = new[] { "  NAME  ", "GEOID" };
        Assert.That(ZipCrosswalkParser.ColIdx(header, "NAME"), Is.EqualTo(0));
    }

    [Test]
    public void ColIdx_MissingColumn_ReturnsNegativeOne()
    {
        var header = new[] { "A", "B", "C" };
        Assert.That(ZipCrosswalkParser.ColIdx(header, "MISSING"), Is.EqualTo(-1));
    }

    [Test]
    public void ColIdx_EmptyHeader_ReturnsNegativeOne()
        => Assert.That(ZipCrosswalkParser.ColIdx(Array.Empty<string>(), "X"), Is.EqualTo(-1));

    // ── StripPlaceSuffix ──────────────────────────────────────────────────────

    [Test]
    public void StripPlaceSuffix_City_Stripped()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("Springfield city"), Is.EqualTo("Springfield"));

    [Test]
    public void StripPlaceSuffix_Town_Stripped()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("Greenfield town"), Is.EqualTo("Greenfield"));

    [Test]
    public void StripPlaceSuffix_CDP_Stripped()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("Riverside CDP"), Is.EqualTo("Riverside"));

    [Test]
    public void StripPlaceSuffix_Borough_Stripped()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("Old Forge borough"), Is.EqualTo("Old Forge"));

    [Test]
    public void StripPlaceSuffix_Village_Stripped()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("Oak Park village"), Is.EqualTo("Oak Park"));

    [Test]
    public void StripPlaceSuffix_Township_Stripped()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("Maple township"), Is.EqualTo("Maple"));

    [Test]
    public void StripPlaceSuffix_Municipality_Stripped()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("San Juan municipality"), Is.EqualTo("San Juan"));

    [Test]
    public void StripPlaceSuffix_Comunidad_Stripped()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("Barrio comunidad"), Is.EqualTo("Barrio"));

    [Test]
    public void StripPlaceSuffix_ZonaUrbana_Stripped()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("Las Vegas zona urbana"), Is.EqualTo("Las Vegas"));

    [Test]
    public void StripPlaceSuffix_NoSuffix_ReturnedUnchanged()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("Chicago"), Is.EqualTo("Chicago"));

    [Test]
    public void StripPlaceSuffix_SuffixMatchIsCaseInsensitive()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("Austin CITY"), Is.EqualTo("Austin"));

    [Test]
    public void StripPlaceSuffix_MultiWordCityName_PreservesInternalWords()
        => Assert.That(ZipCrosswalkParser.StripPlaceSuffix("New York city"), Is.EqualTo("New York"));

    // ── ParseCountyCrosswalk ──────────────────────────────────────────────────

    private static string CountyFile(params string[] dataRows)
    {
        const string header = "OID|GEOID_ZCTA5_20|EXTRA|GEOID_COUNTY_20|NAMELSAD_COUNTY_20|AREALAND_PART";
        return string.Join("\n", new[] { header }.Concat(dataRows));
    }

    [Test]
    public void ParseCountyCrosswalk_SingleRow_Parsed()
    {
        var content = CountyFile("1|90001|x|06037|Los Angeles County|5000000");
        var result  = ZipCrosswalkParser.ParseCountyCrosswalk(content);

        Assert.That(result, Does.ContainKey("90001"));
        Assert.That(result["90001"].CountyFips, Is.EqualTo("06037"));
        Assert.That(result["90001"].CountyName, Is.EqualTo("Los Angeles County"));
    }

    [Test]
    public void ParseCountyCrosswalk_MultipleRowsSameZcta_PicksLargestArea()
    {
        var content = CountyFile(
            "1|90001|x|06037|LA County|1000000",
            "2|90001|x|06059|Orange County|9000000");  // larger area
        var result = ZipCrosswalkParser.ParseCountyCrosswalk(content);

        Assert.That(result["90001"].CountyFips, Is.EqualTo("06059"),
            "Should pick Orange County (larger AREALAND_PART)");
    }

    [Test]
    public void ParseCountyCrosswalk_RowWithShortZcta_Skipped()
    {
        var content = CountyFile("1|9001|x|06037|LA County|1000000");  // 4-digit ZCTA
        var result  = ZipCrosswalkParser.ParseCountyCrosswalk(content);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseCountyCrosswalk_RowWithShortFips_Skipped()
    {
        var content = CountyFile("1|90001|x|6037|LA County|1000000");  // 4-digit FIPS
        var result  = ZipCrosswalkParser.ParseCountyCrosswalk(content);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void ParseCountyCrosswalk_MissingAreaColumn_DefaultsToZero()
    {
        const string header = "OID|GEOID_ZCTA5_20|GEOID_COUNTY_20|NAMELSAD_COUNTY_20";
        var content = header + "\n1|90001|06037|LA County";
        var result  = ZipCrosswalkParser.ParseCountyCrosswalk(content);

        Assert.That(result, Does.ContainKey("90001"),
            "Rows without AREALAND_PART still parse (area defaults to 0)");
    }

    [Test]
    public void ParseCountyCrosswalk_EmptyContent_ReturnsEmpty()
        => Assert.That(ZipCrosswalkParser.ParseCountyCrosswalk(""), Is.Empty);

    [Test]
    public void ParseCountyCrosswalk_HeaderOnly_ReturnsEmpty()
        => Assert.That(ZipCrosswalkParser.ParseCountyCrosswalk(
            "OID|GEOID_ZCTA5_20|GEOID_COUNTY_20|AREALAND_PART"), Is.Empty);

    [Test]
    public void ParseCountyCrosswalk_MissingRequiredColumns_ReturnsEmpty()
        => Assert.That(ZipCrosswalkParser.ParseCountyCrosswalk("COLX|COLY\n1|2"), Is.Empty);

    [Test]
    public void ParseCountyCrosswalk_MultipleDistinctZctas_AllPresent()
    {
        var content = CountyFile(
            "1|90001|x|06037|LA County|1000000",
            "2|10001|x|36061|New York County|2000000");
        var result = ZipCrosswalkParser.ParseCountyCrosswalk(content);

        Assert.That(result.Keys, Is.EquivalentTo(new[] { "90001", "10001" }));
    }

    // ── ParsePlaceCrosswalk ───────────────────────────────────────────────────

    private static string PlaceFile(params string[] dataRows)
    {
        const string header = "OID|GEOID_ZCTA5_20|EXTRA|NAMELSAD_PLACE_20|AREALAND_PART";
        return string.Join("\n", new[] { header }.Concat(dataRows));
    }

    [Test]
    public void ParsePlaceCrosswalk_SingleRow_StripsSuffix()
    {
        var content = PlaceFile("1|90001|x|Los Angeles city|5000000");
        var result  = ZipCrosswalkParser.ParsePlaceCrosswalk(content);

        Assert.That(result, Does.ContainKey("90001"));
        Assert.That(result["90001"], Is.EqualTo("Los Angeles"),
            "City suffix should be stripped");
    }

    [Test]
    public void ParsePlaceCrosswalk_MultipleRowsSameZcta_PicksLargestArea()
    {
        var content = PlaceFile(
            "1|90001|x|Culver City city|1000000",
            "2|90001|x|Los Angeles city|9000000");
        var result = ZipCrosswalkParser.ParsePlaceCrosswalk(content);

        Assert.That(result["90001"], Is.EqualTo("Los Angeles"));
    }

    [Test]
    public void ParsePlaceCrosswalk_ShortZcta_Skipped()
    {
        var content = PlaceFile("1|9001|x|Springfield city|100");
        Assert.That(ZipCrosswalkParser.ParsePlaceCrosswalk(content), Is.Empty);
    }

    [Test]
    public void ParsePlaceCrosswalk_EmptyName_Skipped()
    {
        var content = PlaceFile("1|90001|x||100");
        Assert.That(ZipCrosswalkParser.ParsePlaceCrosswalk(content), Is.Empty);
    }

    [Test]
    public void ParsePlaceCrosswalk_EmptyContent_ReturnsEmpty()
        => Assert.That(ZipCrosswalkParser.ParsePlaceCrosswalk(""), Is.Empty);

    [Test]
    public void ParsePlaceCrosswalk_MissingRequiredColumns_ReturnsEmpty()
        => Assert.That(ZipCrosswalkParser.ParsePlaceCrosswalk("COLX|COLY\n1|2"), Is.Empty);

    [Test]
    public void ParsePlaceCrosswalk_MultipleZctas_AllPresent()
    {
        var content = PlaceFile(
            "1|90001|x|Los Angeles city|5000000",
            "2|10001|x|New York city|8000000");
        var result = ZipCrosswalkParser.ParsePlaceCrosswalk(content);

        Assert.That(result.Keys, Is.EquivalentTo(new[] { "90001", "10001" }));
    }
}
