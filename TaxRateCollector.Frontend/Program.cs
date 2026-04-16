using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Scrapers;
using TaxRateCollector.Infrastructure.Scrapers.Strategies;
using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.Infrastructure.Seeding;
using TaxRateCollector.Frontend.Components;
using TaxRateCollector.Frontend.Logging;

// Lazy reference to the DI container — filled after app.Build()
IServiceProvider? serviceProvider = null;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.Sink(new EfCoreSink(() => serviceProvider))
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ── Blazor ────────────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();

// ── EF Core + SQLite ──────────────────────────────────────────────────────────
var dbPath = Path.Combine(builder.Environment.ContentRootPath, "taxrates.db");
var connStr = $"Data Source={dbPath}";

// Factory for Blazor components (scoped per-use)
builder.Services.AddDbContextFactory<AppDbContext>(opts => opts.UseSqlite(connStr));
// Scoped registration required by ASP.NET Core Identity stores
builder.Services.AddScoped<AppDbContext>(sp =>
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// ── Identity ──────────────────────────────────────────────────────────────────
builder.Services.AddIdentityCore<IdentityUser>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddSignInManager()
.AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/login";
});

builder.Services.AddAuthorization();

// ── HttpClient for scrapers + PayPal ──────────────────────────────────────────
builder.Services.AddHttpClient();

// ── Scraper strategies ────────────────────────────────────────────────────────
builder.Services.AddScoped<IScrapeStrategy, IllinoisTableScraper>();
builder.Services.AddScoped<IScrapeStrategy, CaliforniaCsvScraper>();
builder.Services.AddScoped<IScrapeStrategy, TexasExcelScraper>();

// ── Core services ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IDiffEngine, DiffEngine>();
builder.Services.AddScoped<IScrapeOrchestrator, ScrapeOrchestrator>();
builder.Services.AddScoped<ITaxCalculator, TaxCalculator>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<IPayPalService, PayPalService>();
builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();
builder.Services.AddScoped<IWebDirectoryScanner, WebDirectoryScanner>();
builder.Services.AddScoped<IZipImportService, ZipImportService>();
builder.Services.AddScoped<ICensusJurisdictionImportService, CensusJurisdictionImportService>();

// ── App settings (singleton — %APPDATA%\MindAttic\TaxRateCollector\settings.json) ──
var settings = new SettingsService();
settings.Load();
builder.Services.AddSingleton(settings);

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Wire Serilog EF sink to the live DI container ────────────────────────────
serviceProvider = app.Services;

// ── Migrate + seed on startup ─────────────────────────────────────────────────
var dbFactory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
await using (var db = await dbFactory.CreateDbContextAsync())
{
    await db.Database.MigrateAsync();
    await JurisdictionSeeder.SeedAsync(db);
    await TaxCategorySeeder.SeedAsync(db);

    // Seed singleton admin config rows if they don't exist yet
    if (!await db.PricingConfigs.AnyAsync())
    {
        db.PricingConfigs.Add(new PricingConfig
        {
            Id = 1,
            PricePerState = 0.01m,
            Currency = "USD",
            UpdatedAt = DateTime.UtcNow.ToString("o")
        });
        await db.SaveChangesAsync();
    }
    if (!await db.PayPalConfigs.AnyAsync())
    {
        db.PayPalConfigs.Add(new PayPalConfig
        {
            Id = 1,
            Mode = "sandbox",
            UpdatedAt = DateTime.UtcNow.ToString("o")
        });
        await db.SaveChangesAsync();
    }
}

// ── Dev admin user seeding ────────────────────────────────────────────────────
var devAdminEmail = builder.Configuration["DEV_ADMIN_EMAIL"];
var devAdminPassword = builder.Configuration["DEV_ADMIN_PASSWORD"];
if (!string.IsNullOrEmpty(devAdminEmail) && !string.IsNullOrEmpty(devAdminPassword))
{
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    if (await userManager.FindByEmailAsync(devAdminEmail) == null)
    {
        var adminUser = new IdentityUser { UserName = devAdminEmail, Email = devAdminEmail, EmailConfirmed = true };
        await userManager.CreateAsync(adminUser, devAdminPassword);
    }
}

// ── Middleware ────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Evidence file serving ─────────────────────────────────────────────────────
app.MapGet("/evidence/{filename}", async (string filename, HttpContext ctx) =>
{
    if (filename.Contains('/') || filename.Contains('\\') || filename.Contains(".."))
        return Results.BadRequest();

    var path = Path.Combine(SettingsService.EvidenceDirectory, filename);
    if (!File.Exists(path)) return Results.NotFound();

    var ext = Path.GetExtension(filename).ToLowerInvariant();
    var mime = ext switch
    {
        ".pdf"            => "application/pdf",
        ".txt"            => "text/plain",
        ".csv"            => "text/csv",
        ".html" or ".htm" => "text/html",
        ".json"           => "application/json",
        ".xlsx"           => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _                 => "application/octet-stream"
    };
    return Results.File(path, mime, filename);
}).RequireAuthorization();

// ── Dev auto-login (Development only) ────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev-login", async (
        HttpContext ctx,
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager,
        IConfiguration config) =>
    {
        var email = config["DEV_ADMIN_EMAIL"];
        if (string.IsNullOrEmpty(email)) return Results.Redirect("/login");
        var user = await userManager.FindByEmailAsync(email);
        if (user == null) return Results.Redirect("/login");
        await signInManager.SignInAsync(user, isPersistent: false);
        return Results.Redirect("/");
    });
}

// ── Blazor ────────────────────────────────────────────────────────────────────
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
