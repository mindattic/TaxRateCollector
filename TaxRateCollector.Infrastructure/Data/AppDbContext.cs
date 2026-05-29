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
    public DbSet<ChangeLogEntry> ChangeLog => Set<ChangeLogEntry>();
    public DbSet<ScrapeRun> ScrapeRuns => Set<ScrapeRun>();

    // ── ZIP code lookup ───────────────────────────────────────────────────────
    public DbSet<ZipCodeRecord> ZipCodes => Set<ZipCodeRecord>();
    public DbSet<ZipCodeDistrict> ZipCodeDistricts => Set<ZipCodeDistrict>();

    // ── Product / service tax categories ─────────────────────────────────────
    public DbSet<TaxCategory> TaxCategories => Set<TaxCategory>();
    public DbSet<TaxCategoryRule> TaxCategoryRules => Set<TaxCategoryRule>();

    // ── State tax profiles ────────────────────────────────────────────────────
    public DbSet<StateTaxProfile> StateTaxProfiles => Set<StateTaxProfile>();
    public DbSet<StateCategoryRule> StateCategoryRules => Set<StateCategoryRule>();

    // ── Application logs ──────────────────────────────────────────────────────
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();

    // ── Subscription / billing ────────────────────────────────────────────────
    public DbSet<PricingConfig> PricingConfigs => Set<PricingConfig>();
    public DbSet<PayPalConfig> PayPalConfigs => Set<PayPalConfig>();
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();
    public DbSet<SubscribedState> SubscribedStates => Set<SubscribedState>();
    public DbSet<SubscribedCategory> SubscribedCategories => Set<SubscribedCategory>();
    public DbSet<BillingRecord> BillingRecords => Set<BillingRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

        // ── Tax rates (1:M per jurisdiction — one named law per row) ──────────
        modelBuilder.Entity<TaxRate>(e =>
        {
            e.Property(t => t.Rate).HasPrecision(18, 6);
            e.Property(t => t.MinAbv).HasPrecision(18, 6);
            e.Property(t => t.MaxAbv).HasPrecision(18, 6);
            e.Property(t => t.MinTaxableAmount).HasPrecision(18, 2);
            e.Property(t => t.MaxTaxableAmount).HasPrecision(18, 2);
            e.Property(t => t.FlatCapPerUnit).HasPrecision(18, 6);
            e.Property(t => t.AdjustmentFrequency).HasConversion<string>();
            e.Property(t => t.RateBasis).HasConversion<string>();
            e.Property(t => t.SaleContext).HasConversion<string>();
            e.Property(t => t.RemittancePoint).HasConversion<string>();
            e.Property(t => t.TaxType).HasConversion<string>();
            e.Property(t => t.ProductCategory).HasConversion<string>();

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

            e.HasOne(t => t.TaxCategory)
             .WithMany()
             .HasForeignKey(t => t.TaxCategoryId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.Restrict);

            // AutoApprove is stored as the inverted NeedsReview column (0 = pending, 1 = approved)
            e.Property(t => t.AutoApprove)
             .HasColumnName("NeedsReview")
             .HasConversion(v => !v, v => !v);

            e.HasIndex(t => new { t.JurisdictionId, t.TaxCategoryId, t.IsCurrent })
             .HasDatabaseName("IX_TaxRates_JurisdictionId_CategoryId_Current");

            e.HasIndex(t => new { t.JurisdictionId, t.ProductCategory, t.TaxType, t.IsCurrent })
             .HasDatabaseName("IX_TaxRates_JurisdictionId_ProductCategory_TaxType_Current");
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
            e.HasOne(c => c.TaxRate)
             .WithMany()
             .HasForeignKey(c => c.TaxRateId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.ClientSetNull);
            e.Property(c => c.ChangeType).HasConversion<string>();
            e.Property(c => c.OldRate).HasPrecision(18, 6);
            e.Property(c => c.NewRate).HasPrecision(18, 6);
        });

        modelBuilder.Entity<ScrapeRun>(e =>
        {
            e.Property(r => r.Status).HasConversion<string>();
        });

        // ── ZIP code records & district junction ──────────────────────────────
        modelBuilder.Entity<ZipCodeRecord>(e =>
        {
            e.HasKey(z => z.ZipCode);
            e.HasIndex(z => z.StateFips);
            e.HasIndex(z => z.CountyFips);
            e.HasIndex(z => z.StateCode);
            e.HasMany(z => z.Districts)
             .WithOne(d => d.ZipCodeRecord)
             .HasForeignKey(d => d.ZipCode)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ZipCodeDistrict>(e =>
        {
            e.HasIndex(d => new { d.ZipCode, d.JurisdictionId }).IsUnique();
            e.HasOne(d => d.Jurisdiction)
             .WithMany(j => j.ZipCodeDistricts)
             .HasForeignKey(d => d.JurisdictionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Subscription / billing ────────────────────────────────────────────
        modelBuilder.Entity<Subscriber>(e =>
        {
            e.HasIndex(s => s.UserId);
            e.HasMany(s => s.SubscribedStates)
             .WithOne(ss => ss.Subscriber)
             .HasForeignKey(ss => ss.SubscriberId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(s => s.SubscribedCategories)
             .WithOne(sc => sc.Subscriber)
             .HasForeignKey(sc => sc.SubscriberId)
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

        modelBuilder.Entity<SubscribedCategory>(e =>
        {
            e.HasIndex(sc => new { sc.SubscriberId, sc.CategoryId });
        });

        modelBuilder.Entity<BillingRecord>(e =>
        {
            e.Property(b => b.Status).HasConversion<string>();
            e.Property(b => b.PricePerState).HasPrecision(18, 2);
            e.Property(b => b.Subtotal).HasPrecision(18, 2);
            // TaxRate is a rate fraction (e.g. 0.08875), not a dollar amount — needs
            // the same scale as every other rate column or it rounds to 2 decimals.
            e.Property(b => b.TaxRate).HasPrecision(18, 6);
            e.Property(b => b.TaxAmount).HasPrecision(18, 2);
            e.Property(b => b.Total).HasPrecision(18, 2);
        });

        modelBuilder.Entity<PricingConfig>(e =>
        {
            e.Property(p => p.PricePerState).HasPrecision(18, 2);
            e.Property(p => p.PricePerCategory).HasPrecision(18, 2);
        });

        // ── Tax categories ────────────────────────────────────────────────────
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
            e.Property(r => r.OverrideRate).HasPrecision(18, 6);
            e.Property(r => r.Taxability).HasConversion<string>();
            e.HasOne(r => r.TaxCategory)
             .WithMany(c => c.Rules)
             .HasForeignKey(r => r.TaxCategoryId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.Jurisdiction)
             .WithMany()
             .HasForeignKey(r => r.JurisdictionId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── State tax profiles ────────────────────────────────────────────────
        modelBuilder.Entity<StateTaxProfile>(e =>
        {
            e.HasIndex(p => p.StateCode).IsUnique();
            e.Property(p => p.LocalTaxAuthorityType).HasConversion<string>();
            e.Property(p => p.GeneralSalesTaxRate).HasPrecision(18, 6);
            e.Property(p => p.LocalRateCap).HasPrecision(18, 6);
            e.Property(p => p.EconomicNexusThresholdAmount).HasPrecision(18, 2);
            e.Property(p => p.IntrastateSourcingRule).HasConversion<string>();
        });

        modelBuilder.Entity<StateCategoryRule>(e =>
        {
            e.HasIndex(r => new { r.StateTaxProfileId, r.TaxCategoryId }).IsUnique();
            e.Property(r => r.StateRate).HasPrecision(18, 6);
            e.Property(r => r.Taxability).HasConversion<string>();
            e.HasOne(r => r.StateTaxProfile)
             .WithMany(p => p.CategoryRules)
             .HasForeignKey(r => r.StateTaxProfileId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.TaxCategory)
             .WithMany()
             .HasForeignKey(r => r.TaxCategoryId)
             .IsRequired(false)
             .OnDelete(DeleteBehavior.SetNull);
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
