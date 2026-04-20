using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Core.Options;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Scrapers;
using TaxRateCollector.Infrastructure.Scrapers.Strategies;
using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.Infrastructure.Seeding;
using TaxRateCollector.Frontend.Components;
using TaxRateCollector.Frontend.Logging;
using TaxRateCollector.Frontend.Services;

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
builder.Services.AddHostedService<ConsoleHeartbeatService>();

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
    Console.WriteLine("[startup] Applying migrations…");
    await db.Database.MigrateAsync();

    Console.WriteLine("[startup] Seeding SST taxonomy…");
    await TaxCategorySeeder.SeedAsync(db);

    Console.WriteLine("[startup] Seeding jurisdictions…");
    await JurisdictionSeeder.SeedAsync(db);

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

// ── Roles + admin + demo subscribers ─────────────────────────────────────────
{
    using var scope = app.Services.CreateScope();
    var userManager  = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    var roleManager  = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var dbFactory2   = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

    // Ensure Administrator role exists
    if (!await roleManager.RoleExistsAsync("Administrator"))
        await roleManager.CreateAsync(new IdentityRole("Administrator"));

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
