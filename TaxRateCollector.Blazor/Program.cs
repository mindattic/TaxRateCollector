using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Core.Options;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Scrapers;
using TaxRateCollector.Infrastructure.Scrapers.Strategies;
using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.Infrastructure.Seeding;
using TaxRateCollector.Blazor.Components;
using TaxRateCollector.Blazor.Logging;
using TaxRateCollector.Blazor.Services;

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

// ── EF Core + SQL Server ──────────────────────────────────────────────────────
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContextFactory<AppDbContext>(opts => opts.UseSqlServer(connStr));
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
.AddRoles<IdentityRole>()
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

// ── Bulk state scrapers ───────────────────────────────────────────────────────
builder.Services.AddScoped<IStateBulkScraper, WisconsinAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, WisconsinSalesTaxScraper>();
builder.Services.AddScoped<IStateBulkScraper, IllinoisAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, MinnesotaAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, IowaAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, IndianaAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, MichiganAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, NorthDakotaAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, SouthDakotaAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, OhioAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, MontanaAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, IdahoAlcoholScraper>();
builder.Services.AddScoped<IStateBulkScraper, OregonAlcoholScraper>();

// ── Core services ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IDiffEngine, DiffEngine>();
builder.Services.AddScoped<IScrapeOrchestrator, ScrapeOrchestrator>();
builder.Services.AddScoped<IEvidenceFileStore, EvidenceFileStore>();
builder.Services.AddSingleton<ScrapeJobCoordinator>();
builder.Services.AddHostedService<ScrapeWorkerService>();
builder.Services.AddScoped<ITaxCalculator, TaxCalculator>();
builder.Services.AddScoped<AlertService>();
builder.Services.AddScoped<IPayPalService, PayPalService>();
builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.Section));
builder.Services.AddScoped<IRateLawExtractor, ClaudeRateLawExtractor>();
builder.Services.AddScoped<IRecursiveRateScraper, RecursiveRateScraper>();
builder.Services.AddScoped<IWebDirectoryScanner, WebDirectoryScanner>();
builder.Services.AddScoped<IZipImportService, ZipImportService>();
builder.Services.AddScoped<ICensusJurisdictionImportService, CensusJurisdictionImportService>();
builder.Services.AddScoped<ISstTaxonomyImportService, SstTaxonomyImportService>();
builder.Services.AddScoped<ViewAsService>();

// ── App settings (singleton — %APPDATA%\MindAttic\TaxRateCollector\settings.json) ──
var settings = new SettingsService();
settings.Load();
builder.Services.AddSingleton(settings);

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Wire Serilog EF sink to the live DI container ────────────────────────────
serviceProvider = app.Services;

bool populateMode  = args.Contains("--populate");
bool zeroRatesMode = args.Contains("--zero-rates");
bool scrapeMode    = args.Contains("--scrape");

// ── Migrate + seed on startup ─────────────────────────────────────────────────
var dbFactory = app.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();
await using (var db = await dbFactory.CreateDbContextAsync())
{
    Console.WriteLine("[startup] Applying migrations…");
    await db.Database.MigrateAsync();

    if (zeroRatesMode)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM SourceDocuments");
        await db.Database.ExecuteSqlRawAsync("UPDATE TaxRates SET Rate = 0");
        Console.WriteLine("[zero-rates] SourceDocuments cleared. TaxRate.Rate set to 0.0.");

        var evidenceDir = TaxRateCollector.Infrastructure.Services.SettingsService.EvidenceDirectory;
        if (Directory.Exists(evidenceDir))
        {
            var files = Directory.GetFiles(evidenceDir);
            foreach (var f in files) File.Delete(f);
            Console.WriteLine($"[zero-rates] Deleted {files.Length} evidence file(s) from {evidenceDir}");
        }
        else
        {
            Console.WriteLine("[zero-rates] Evidence directory does not exist — nothing to delete.");
        }
        return;
    }

    if (populateMode)
    {
        Console.WriteLine("[populate] Truncating all existing data for fresh import…");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM BillingRecords");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM SubscribedStates");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM Subscribers");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM LogEntries");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ChangeLog");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM SourceDocuments");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM TaxRates");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ScrapeRuns");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM ZipCodes");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM TaxCategoryRules");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE TaxCategories NOCHECK CONSTRAINT ALL");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM TaxCategories");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE TaxCategories WITH CHECK CHECK CONSTRAINT ALL");
        foreach (var jType in new[] { JurisdictionType.City, JurisdictionType.County, JurisdictionType.State, JurisdictionType.Country })
        {
            var ids = await db.Jurisdictions.Where(j => j.JurisdictionType == jType).Select(j => j.Id).ToListAsync();
            if (ids.Count > 0)
                await db.Jurisdictions.Where(j => ids.Contains(j.Id)).ExecuteDeleteAsync();
        }
        await db.Database.ExecuteSqlRawAsync("DELETE FROM StateTaxProfiles");
        Console.WriteLine("[populate] Wipe complete — starting full import…");
    }

    Console.WriteLine("[startup] Seeding SST taxonomy…");
    await TaxCategorySeeder.SeedAsync(db);

    Console.WriteLine("[startup] Seeding jurisdictions…");
    await JurisdictionSeeder.SeedAsync(db);

    Console.WriteLine("[startup] Ensuring Census counties + cities…");
    {
        var countyCount = await db.Jurisdictions.CountAsync(j => j.JurisdictionType == JurisdictionType.County);
        var cityCount   = await db.Jurisdictions.CountAsync(j => j.JurisdictionType == JurisdictionType.City);

        if (countyCount >= 3000 && cityCount >= 5000)
        {
            Console.WriteLine($"[startup] Census data present ({countyCount} counties, {cityCount} cities) — skipped.");
        }
        else
        {
            if (countyCount < 3000)
            {
                Console.WriteLine($"[startup] {countyCount} counties found — wiping and re-importing Census data…");
                await db.ZipCodes
                    .Where(z => z.CountyJurisdictionId != null || z.CityJurisdictionId != null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(z => z.CountyJurisdictionId, (int?)null)
                        .SetProperty(z => z.CityJurisdictionId,   (int?)null));
                var cityIds = await db.Jurisdictions
                    .Where(j => j.JurisdictionType == JurisdictionType.City)
                    .Select(j => j.Id).ToListAsync();
                if (cityIds.Count > 0)
                    await db.Jurisdictions.Where(j => cityIds.Contains(j.Id)).ExecuteDeleteAsync();
                var countyIds = await db.Jurisdictions
                    .Where(j => j.JurisdictionType == JurisdictionType.County)
                    .Select(j => j.Id).ToListAsync();
                if (countyIds.Count > 0)
                    await db.Jurisdictions.Where(j => countyIds.Contains(j.Id)).ExecuteDeleteAsync();
            }
            else
            {
                Console.WriteLine($"[startup] {countyCount} counties OK but only {cityCount} cities — importing cities only…");
            }

            using var censusScope = app.Services.CreateScope();
            var censusSvc = censusScope.ServiceProvider.GetRequiredService<ICensusJurisdictionImportService>();

            CensusImportProgress? lastCensusSnap = null;
            var censusProgressReporter = new Progress<CensusImportProgress>(p => lastCensusSnap = p);
            var censusSw = System.Diagnostics.Stopwatch.StartNew();
            using var censusTicker = new PeriodicTimer(TimeSpan.FromMinutes(1));
            var censusTickerTask = Task.Run(async () =>
            {
                while (await censusTicker.WaitForNextTickAsync())
                {
                    var p = lastCensusSnap;
                    var pct = (p?.Total > 0) ? $"{p.Processed * 100.0 / p.Total:F1}%" : "?%";
                    var current = p != null ? $"{p.Stage}: {p.Processed:N0}/{(p.Total > 0 ? p.Total.ToString("N0") : "?")} ({pct})" : "starting…";
                    Console.WriteLine($"[startup] Census progress — {current} — elapsed {censusSw.Elapsed:mm\\:ss}");
                }
            });

            var censusResult = await censusSvc.ImportAsync(censusProgressReporter, CancellationToken.None);
            censusTicker.Dispose();
            await censusTickerTask;
            Console.WriteLine($"[startup] Census import done: {censusResult.CountiesCreated} counties, {censusResult.CitiesCreated} cities, {censusResult.ZipsRelinked} zips in {censusResult.Elapsed:mm\\:ss}.");
        }
    }

    Console.WriteLine("[startup] Ensuring ZIP code crosswalks…");
    {
        var zipCount = await db.ZipCodes.CountAsync();
        if (zipCount >= 30000)
        {
            Console.WriteLine($"[startup] {zipCount:N0} ZIP codes present — skipped.");
        }
        else
        {
            Console.WriteLine($"[startup] {zipCount} ZIP codes found — importing ZIP crosswalks…");
            using var zipScope = app.Services.CreateScope();
            var zipSvc = zipScope.ServiceProvider.GetRequiredService<IZipImportService>();

            ZipImportProgress? lastZipSnap = null;
            var zipProgressReporter = new Progress<ZipImportProgress>(p => lastZipSnap = p);
            var zipSw = System.Diagnostics.Stopwatch.StartNew();
            using var zipTicker = new PeriodicTimer(TimeSpan.FromMinutes(1));
            var zipTickerTask = Task.Run(async () =>
            {
                while (await zipTicker.WaitForNextTickAsync())
                {
                    var p = lastZipSnap;
                    var pct = (p?.Total > 0) ? $"{p.Processed * 100.0 / p.Total:F1}%" : "?%";
                    var current = p != null ? $"{p.Processed:N0}/{(p.Total > 0 ? p.Total.ToString("N0") : "?")} ({pct}) — {p.Imported:N0} imported, {p.Errors} errors" : "starting…";
                    Console.WriteLine($"[startup] ZIP progress — {current} — elapsed {zipSw.Elapsed:mm\\:ss}");
                }
            });

            var zipResult = await zipSvc.ImportAsync(zipProgressReporter, CancellationToken.None);
            zipTicker.Dispose();
            await zipTickerTask;
            Console.WriteLine($"[startup] ZIP import done: {zipResult.Imported:N0} imported in {zipResult.Elapsed:mm\\:ss}.");
        }
    }

    Console.WriteLine("[startup] Seeding state tax profiles…");
    await StateTaxProfileSeeder.SeedAsync(db);

    Console.WriteLine("[startup] Seeding config…");
    if (!await db.PricingConfigs.AnyAsync())
    {
        db.PricingConfigs.Add(new PricingConfig
        {
            PricePerState = 0.01m,
            PricePerCategory = 0.01m,
            Currency = "USD",
            UpdatedAt = DateTime.UtcNow.ToString("o")
        });
        await db.SaveChangesAsync();
    }
    if (!await db.PayPalConfigs.AnyAsync())
    {
        db.PayPalConfigs.Add(new PayPalConfig
        {
            Mode = "sandbox",
            UpdatedAt = DateTime.UtcNow.ToString("o")
        });
        await db.SaveChangesAsync();
    }

    Console.WriteLine("[startup] DB ready.");
}

if (populateMode)
{
    Console.WriteLine("[populate] All done. Start the app normally to serve requests.");
    return;
}

if (scrapeMode)
{
    var stateArgList = GetArgList(args, "--state");
    var categoryArg  = GetArg(args, "--category");

    if (stateArgList.Length == 0)
    {
        Console.Error.WriteLine("[scrape] Usage: --scrape --state <WI|IL|MN|…> [<state2> …]");
        Console.Error.WriteLine("         Use --state all to run every registered scraper.");
        return;
    }

    using var scrapeScope = app.Services.CreateScope();

    // Resolve 'all' to every registered bulk scraper
    var registeredScrapers = scrapeScope.ServiceProvider.GetServices<IStateBulkScraper>().ToArray();
    var statesToRun = stateArgList.Length == 1
                      && stateArgList[0].Equals("all", StringComparison.OrdinalIgnoreCase)
        ? registeredScrapers.Select(s => s.StateCode.ToUpperInvariant()).ToArray()
        : stateArgList.Select(s => s.ToUpperInvariant()).ToArray();

    await using var scrapeDb = await scrapeScope.ServiceProvider
        .GetRequiredService<IDbContextFactory<AppDbContext>>()
        .CreateDbContextAsync();

    int? taxCategoryId = null;
    if (!string.IsNullOrEmpty(categoryArg))
    {
        var categoryLower = categoryArg.ToLowerInvariant();
        var category = await scrapeDb.TaxCategories
            .Where(c => EF.Functions.Like(c.Name.ToLower(), $"%{categoryLower}%"))
            .FirstOrDefaultAsync();
        if (category is null)
            Console.WriteLine($"[scrape] Warning: no TaxCategory matching '{categoryArg}' — rates saved without category.");
        else
        {
            taxCategoryId = category.Id;
            Console.WriteLine($"[scrape] Matched category: '{category.Name}' (id={category.Id})");
        }
    }

    var orchestrator   = scrapeScope.ServiceProvider.GetRequiredService<IScrapeOrchestrator>();
    var scrapeSettings = scrapeScope.ServiceProvider.GetRequiredService<SettingsService>();

    Console.WriteLine($"[scrape] Running {statesToRun.Length} scraper(s): {string.Join(", ", statesToRun)}");

    int totalSaved = 0, totalErrors = 0;
    foreach (var state in statesToRun)
    {
        Console.Write($"[scrape] [{state}] Starting… ");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var saved = await orchestrator.RunBulkForStateAsync(state, taxCategoryId,
                needsReview: !scrapeSettings.Current.AutoApprove);
            sw.Stop();
            totalSaved += saved;
            Console.WriteLine($"{saved} rates saved ({sw.Elapsed.TotalSeconds:F1}s)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            totalErrors++;
            Console.Error.WriteLine($"ERROR: {ex.Message}");
        }
    }

    if (statesToRun.Length > 1)
        Console.WriteLine($"[scrape] All done — {totalSaved} rates saved, {totalErrors} error(s).");

    return;
}

static string? GetArg(string[] a, string flag)
{
    var idx = Array.IndexOf(a, flag);
    return idx >= 0 && idx + 1 < a.Length ? a[idx + 1] : null;
}

// Returns all values after the flag until the next flag (starts with --).
static string[] GetArgList(string[] a, string flag)
{
    var idx = Array.IndexOf(a, flag);
    if (idx < 0) return [];
    var values = new List<string>();
    for (int i = idx + 1; i < a.Length; i++)
    {
        if (a[i].StartsWith("--")) break;
        values.Add(a[i]);
    }
    return values.ToArray();
}

// ── Roles + admin + demo subscribers ─────────────────────────────────────────
{
    using var scope = app.Services.CreateScope();
    var userManager  = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager  = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var dbFactory2   = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    // Ensure built-in roles exist
    if (!await roleManager.RoleExistsAsync("Administrator"))
        await roleManager.CreateAsync(new IdentityRole("Administrator"));
    if (!await roleManager.RoleExistsAsync("Approver"))
        await roleManager.CreateAsync(new IdentityRole("Approver"));

    // Seed admin user from config
    var devAdminEmail    = builder.Configuration["DEV_ADMIN_EMAIL"];
    var devAdminPassword = builder.Configuration["DEV_ADMIN_PASSWORD"];
    if (!string.IsNullOrEmpty(devAdminEmail) && !string.IsNullOrEmpty(devAdminPassword))
    {
        var adminUser = await userManager.FindByEmailAsync(devAdminEmail);
        if (adminUser == null)
        {
            adminUser = new IdentityUser { UserName = devAdminEmail, Email = devAdminEmail, EmailConfirmed = true };
            await userManager.CreateAsync(adminUser, devAdminPassword);
            Console.WriteLine($"[seed] Created admin user: {devAdminEmail}");
        }
        if (!await userManager.IsInRoleAsync(adminUser, "Administrator"))
            await userManager.AddToRoleAsync(adminUser, "Administrator");
    }

    // Seed demo subscribers
    Console.WriteLine("[seed] Seeding demo subscribers…");
    await using var db2 = await dbFactory2.CreateDbContextAsync();
    await DemoSubscriberSeeder.SeedAsync(db2, userManager);
    Console.WriteLine("[seed] Demo subscribers ready.");
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
    // Only force download for binary spreadsheets; everything else renders inline
    return ext == ".xlsx"
        ? Results.File(path, mime, filename)
        : Results.File(path, mime);
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

// ── Auth endpoints (Blazor Server can't set auth cookies mid-stream) ─────────
app.MapPost("/login-submit", async (HttpContext ctx, SignInManager<IdentityUser> signIn) =>
{
    var email    = ctx.Request.Form["email"].ToString();
    var password = ctx.Request.Form["password"].ToString();
    var returnUrl = ctx.Request.Form["returnUrl"].ToString();

    var result = await signIn.PasswordSignInAsync(email, password, isPersistent: true, lockoutOnFailure: true);

    if (result.Succeeded)
    {
        var target = !string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : "/";
        ctx.Response.Redirect(target);
    }
    else if (result.IsLockedOut)
        ctx.Response.Redirect("/login?error=locked");
    else
        ctx.Response.Redirect("/login?error=invalid");
});

app.MapPost("/logout", async (HttpContext ctx, SignInManager<IdentityUser> signIn) =>
{
    await signIn.SignOutAsync();
    ctx.Response.Redirect("/login");
});

// ── Blazor ────────────────────────────────────────────────────────────────────
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
