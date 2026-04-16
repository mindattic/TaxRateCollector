using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;

namespace TaxRateCollector.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    // ── Tax rate data ─────────────────────────────────────────────────────────
    public DbSet<Jurisdiction> Jurisdictions => Set<Jurisdiction>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<SourceDocument> SourceDocuments => Set<SourceDocument>();
    public DbSet<ExciseTaxRate> ExciseTaxRates => Set<ExciseTaxRate>();
    public DbSet<ExciseSourceDocument> ExciseSourceDocuments => Set<ExciseSourceDocument>();
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();
    public DbSet<ScrapeRun> ScrapeRuns => Set<ScrapeRun>();

    // ── ZIP code lookup ───────────────────────────────────────────────────────
    public DbSet<ZipCodeRecord> ZipCodes => Set<ZipCodeRecord>();

    // ── Product / service tax categories ─────────────────────────────────────
    public DbSet<TaxCategory> TaxCategories => Set<TaxCategory>();
    public DbSet<TaxCategoryRule> TaxCategoryRules => Set<TaxCategoryRule>();

    // ── Application logs ──────────────────────────────────────────────────────
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();

    // ── Subscription / billing ────────────────────────────────────────────────
    public DbSet<PricingConfig> PricingConfigs => Set<PricingConfig>();
    public DbSet<PayPalConfig> PayPalConfigs => Set<PayPalConfig>();
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();
    public DbSet<SubscribedState> SubscribedStates => Set<SubscribedState>();
    public DbSet<BillingRecord> BillingRecords => Set<BillingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Identity tables must be configured first
        base.OnModelCreating(modelBuilder);

        // ── Jurisdiction hierarchy ────────────────────────────────────────────
        modelBuilder.Entity<Jurisdiction>(e =>
        {
            e.HasIndex(j => j.FipsCode).IsUnique();
            e.Property(j => j.JurisdictionType).HasConversion<string>();
            e.HasOne(j => j.Parent)
             .WithMany(j => j.Children)
             .HasForeignKey(j => j.ParentId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Tax rates ─────────────────────────────────────────────────────────
        modelBuilder.Entity<TaxRate>(e =>
        {
            e.HasOne(t => t.Jurisdiction)
             .WithMany(j => j.TaxRates)
             .HasForeignKey(t => t.JurisdictionId);

            e.HasOne(t => t.ScrapeRun)
             .WithMany(r => r.TaxRates)
             .HasForeignKey(t => t.ScrapeRunId);

            e.HasMany(t => t.SourceDocuments)
             .WithOne(d => d.TaxRate)
             .HasForeignKey(d => d.TaxRateId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SourceDocument>(e =>
        {
            e.Property(d => d.SourceType).HasConversion<string>();
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

        modelBuilder.Entity<ExciseTaxRate>(e =>
        {
            e.Property(x => x.ProductCategory).HasConversion<string>();
            e.HasOne(x => x.Jurisdiction)
             .WithMany()
             .HasForeignKey(x => x.JurisdictionId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ScrapeRun)
             .WithMany()
             .HasForeignKey(x => x.ScrapeRunId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.SourceDocument)
             .WithOne(d => d.ExciseTaxRate)
             .HasForeignKey<ExciseSourceDocument>(d => d.ExciseTaxRateId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExciseSourceDocument>(e =>
        {
            e.Property(d => d.SourceType).HasConversion<string>();
        });

        // ── Subscription / billing ────────────────────────────────────────────
        modelBuilder.Entity<Subscriber>(e =>
        {
            e.HasIndex(s => s.UserId);
            e.HasMany(s => s.SubscribedStates)
             .WithOne(ss => ss.Subscriber)
             .HasForeignKey(ss => ss.SubscriberId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(s => s.BillingRecords)
             .WithOne(b => b.Subscriber)
             .HasForeignKey(b => b.SubscriberId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SubscribedState>(e =>
        {
            e.HasIndex(ss => new { ss.SubscriberId, ss.StateCode });
        });

        modelBuilder.Entity<BillingRecord>(e =>
        {
            e.Property(b => b.Status).HasConversion<string>();
        });

        // PricingConfig and PayPalConfig have no navigation properties — no extra config needed.

        // ── ZIP code records ──────────────────────────────────────────────────────
        modelBuilder.Entity<ZipCodeRecord>(e =>
        {
            e.HasKey(z => z.ZipCode);
            e.HasIndex(z => z.StateFips);
            e.HasIndex(z => z.CountyFips);
            e.HasIndex(z => z.StateCode);
        });

        // ── Tax categories ────────────────────────────────────────────────────────
        modelBuilder.Entity<TaxCategory>(e =>
        {
            e.HasOne(c => c.Parent)
             .WithMany(c => c.Children)
             .HasForeignKey(c => c.ParentId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(c => c.TopLevelType);
        });

        modelBuilder.Entity<TaxCategoryRule>(e =>
        {
            e.HasIndex(r => new { r.TaxCategoryId, r.JurisdictionId }).IsUnique();
            e.HasOne(r => r.TaxCategory)
             .WithMany(c => c.Rules)
             .HasForeignKey(r => r.TaxCategoryId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Jurisdiction)
             .WithMany()
             .HasForeignKey(r => r.JurisdictionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Application logs ──────────────────────────────────────────────────
        modelBuilder.Entity<LogEntry>(e =>
        {
            e.ToTable("LogEntries");
            e.HasIndex(l => l.Timestamp);
            e.HasIndex(l => l.Level);
        });
    }
}
