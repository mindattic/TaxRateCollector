using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;

namespace TaxRateCollector.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Jurisdiction> Jurisdictions => Set<Jurisdiction>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();
    public DbSet<ScrapeRun> ScrapeRuns => Set<ScrapeRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Jurisdiction>(e =>
        {
            e.HasIndex(j => j.FipsCode).IsUnique();
            e.Property(j => j.JurisdictionType).HasConversion<string>();
        });

        modelBuilder.Entity<TaxRate>(e =>
        {
            e.HasOne(t => t.Jurisdiction)
             .WithMany(j => j.TaxRates)
             .HasForeignKey(t => t.JurisdictionId);

            e.HasOne(t => t.ScrapeRun)
             .WithMany(r => r.TaxRates)
             .HasForeignKey(t => t.ScrapeRunId);
        });

        modelBuilder.Entity<ChangeLogEntry>(e =>
        {
            e.ToTable("ChangeLog");
            e.HasOne(c => c.Jurisdiction)
             .WithMany(j => j.ChangeLogEntries)
             .HasForeignKey(c => c.JurisdictionId);
            e.Property(c => c.ChangeType).HasConversion<string>();
        });

        modelBuilder.Entity<ScrapeRun>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>();
        });
    }
}
