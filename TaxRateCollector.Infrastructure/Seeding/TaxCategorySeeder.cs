using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Seeding;

public static class TaxCategorySeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.TaxCategories.AnyAsync()) return;

        var nameToEntity = new Dictionary<string, TaxCategory>(StringComparer.OrdinalIgnoreCase);

        foreach (var def in SstTaxonomyData.Definitions.Where(d => d.ParentName == null))
        {
            var entity = new TaxCategory
            {
                Name         = def.Name,
                TopLevelType = def.TopLevel,
                IsLeaf       = def.IsLeaf,
                SortOrder    = def.Sort,
                ParentId     = null,
                Description  = def.KnownDescription,
            };
            db.TaxCategories.Add(entity);
            nameToEntity[def.Name] = entity;
        }
        await db.SaveChangesAsync();

        var remaining = SstTaxonomyData.Definitions.Where(d => d.ParentName != null).ToList();
        int maxPasses = 5;
        while (remaining.Count > 0 && maxPasses-- > 0)
        {
            var resolved = new List<(TaxCategoryDef Def, TaxCategory Entity)>();
            foreach (var def in remaining)
            {
                if (!nameToEntity.TryGetValue(def.ParentName!, out var parent)) continue;
                if (parent.Id == 0) continue; // parent not yet persisted — defer to next pass
                var entity = new TaxCategory
                {
                    Name         = def.Name,
                    TopLevelType = def.TopLevel,
                    IsLeaf       = def.IsLeaf,
                    SortOrder    = def.Sort,
                    ParentId     = parent.Id,
                    Description  = def.KnownDescription,
                };
                db.TaxCategories.Add(entity);
                resolved.Add((def, entity));
            }
            if (resolved.Count == 0) break;
            remaining = remaining.Except(resolved.Select(r => r.Def)).ToList();
            await db.SaveChangesAsync();
            foreach (var (_, entity) in resolved)
                nameToEntity[entity.Name] = entity;
        }
    }
}
