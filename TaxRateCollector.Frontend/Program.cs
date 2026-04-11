using Microsoft.EntityFrameworkCore;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Scrapers;
using TaxRateCollector.Infrastructure.Scrapers.Strategies;
using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.Infrastructure.Seeding;
using TaxRateCollector.Frontend.Components;

var builder = WebApplication.CreateBuilder(args);

// Razor / Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// EF Core + SQLite — factory only; services create their own short-lived contexts
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "taxrates.db");
builder.Services.AddDbContextFactory<AppDbContext>(opts =>
    opts.UseSqlite($"Data Source={dbPath}"));

// HttpClient for scrapers
builder.Services.AddHttpClient();

// Scraper strategies
builder.Services.AddScoped<IScrapeStrategy, IllinoisTableScraper>();
builder.Services.AddScoped<IScrapeStrategy, CaliforniaCsvScraper>();
builder.Services.AddScoped<IScrapeStrategy, TexasExcelScraper>();

// Core services
builder.Services.AddScoped<IDiffEngine, DiffEngine>();
builder.Services.AddScoped<IScrapeOrchestrator, ScrapeOrchestrator>();
builder.Services.AddScoped<ITaxCalculator, TaxCalculator>();
builder.Services.AddScoped<AlertService>();

// Background scheduler
builder.Services.AddHostedService<ScrapeSchedulerService>();

var app = builder.Build();

// Migrate + seed on startup
var dbFactory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
await using (var db = await dbFactory.CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
    await JurisdictionSeeder.SeedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
