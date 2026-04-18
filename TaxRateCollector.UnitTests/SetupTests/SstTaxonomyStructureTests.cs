using TaxRateCollector.Infrastructure.Seeding;

namespace TaxRateCollector.UnitTests.SetupTests;

/// <summary>
/// Pure unit tests against the static SstTaxonomyData definition list.
/// No database required.
/// </summary>
[TestFixture]
public class SstTaxonomyStructureTests
{
    private static readonly IReadOnlyList<TaxCategoryDef> Defs = SstTaxonomyData.Definitions;

    [Test]
    public void Has_AtLeastOneDef()
        => Assert.That(Defs, Is.Not.Empty);

    [Test]
    public void Has_ExactlyTwoRoots()
    {
        var roots = Defs.Where(d => d.ParentName == null).ToList();
        Assert.That(roots.Count, Is.EqualTo(2),
            "Expected 2 root nodes (Goods, Services). Found: " + string.Join(", ", roots.Select(r => r.Name)));
    }

    [Test]
    public void Roots_AreGoodsAndServices()
    {
        var rootNames = Defs.Where(d => d.ParentName == null).Select(d => d.Name).ToHashSet();
        Assert.Multiple(() =>
        {
            Assert.That(rootNames, Contains.Item("Goods"));
            Assert.That(rootNames, Contains.Item("Services"));
        });
    }

    [Test]
    public void AllParentNames_Resolve()
    {
        var nameSet = Defs.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var broken = Defs
            .Where(d => d.ParentName != null && !nameSet.Contains(d.ParentName))
            .Select(d => $"'{d.Name}' → parent '{d.ParentName}'")
            .ToList();
        Assert.That(broken, Is.Empty, "Definitions reference parents that don't exist:\n" + string.Join("\n", broken));
    }

    [Test]
    public void NoDuplicateNames()
    {
        var dupes = Defs.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key)
                        .ToList();
        Assert.That(dupes, Is.Empty, "Duplicate definition names: " + string.Join(", ", dupes));
    }

    [Test]
    public void AllLeaves_HaveParents()
    {
        var orphanLeaves = Defs.Where(d => d.IsLeaf && d.ParentName == null).Select(d => d.Name).ToList();
        Assert.That(orphanLeaves, Is.Empty, "Leaf nodes must have a parent: " + string.Join(", ", orphanLeaves));
    }

    [Test]
    public void AllTopLevelTypes_AreGoodsOrServices()
    {
        var invalid = Defs
            .Where(d => d.TopLevel != "Goods" && d.TopLevel != "Services")
            .Select(d => $"'{d.Name}' (TopLevel='{d.TopLevel}')")
            .ToList();
        Assert.That(invalid, Is.Empty, "All definitions must have TopLevel of 'Goods' or 'Services':\n" + string.Join("\n", invalid));
    }

    [Test]
    public void GoodsLeaves_AtLeastFour()
    {
        var count = Defs.Count(d => d.TopLevel == "Goods" && d.IsLeaf);
        Assert.That(count, Is.GreaterThanOrEqualTo(4), $"Expected at least 4 Goods leaf categories, got {count}");
    }

    [Test]
    public void ServicesLeaves_AtLeastFour()
    {
        var count = Defs.Count(d => d.TopLevel == "Services" && d.IsLeaf);
        Assert.That(count, Is.GreaterThanOrEqualTo(4), $"Expected at least 4 Services leaf categories, got {count}");
    }

    [Test]
    public void AllSortOrders_ArePositive()
    {
        var bad = Defs.Where(d => d.Sort <= 0).Select(d => d.Name).ToList();
        Assert.That(bad, Is.Empty, "All definitions must have Sort > 0: " + string.Join(", ", bad));
    }

    [Test]
    public void NoCircularReferences()
    {
        var parentMap = Defs.ToDictionary(d => d.Name, d => d.ParentName, StringComparer.OrdinalIgnoreCase);
        foreach (var def in Defs)
        {
            var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var current = def.Name;
            while (parentMap.TryGetValue(current, out var parent) && parent != null)
            {
                Assert.That(seen.Add(current), Is.True,
                    $"Circular reference at '{current}' in ancestry of '{def.Name}'");
                current = parent;
            }
        }
    }

    [Test]
    public void TopLevelClassification_IsConsistent()
    {
        var nameToTopLevel = Defs.ToDictionary(d => d.Name, d => d.TopLevel, StringComparer.OrdinalIgnoreCase);
        var inconsistent = Defs
            .Where(d => d.ParentName != null && nameToTopLevel.TryGetValue(d.ParentName, out var parentTop) && parentTop != d.TopLevel)
            .Select(d => $"'{d.Name}' (TopLevel={d.TopLevel}) has parent '{d.ParentName}' (TopLevel={nameToTopLevel.GetValueOrDefault(d.ParentName!, "?")})")
            .ToList();
        Assert.That(inconsistent, Is.Empty,
            "Child TopLevel must match parent TopLevel:\n" + string.Join("\n", inconsistent));
    }
}
