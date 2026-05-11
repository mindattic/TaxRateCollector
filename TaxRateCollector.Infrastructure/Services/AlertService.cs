using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

public class AlertService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<ChangeLogEntry>> GetUnacknowledgedAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.ChangeLog
            .Include(c => c.Jurisdiction)
            .Where(c => !c.Acknowledged)
            .OrderByDescending(c => c.DetectedAt)
            .ToListAsync();
    }

    public async Task AcknowledgeAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var entry = await db.ChangeLog.FindAsync(id);
        if (entry is null) return;
        entry.Acknowledged = true;
        await db.SaveChangesAsync();
    }

    public async Task AcknowledgeAllAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.ChangeLog
            .Where(c => !c.Acknowledged)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Acknowledged, true));
    }
}
