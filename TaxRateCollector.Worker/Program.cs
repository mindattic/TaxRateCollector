using MindAttic.Legion;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Core.Options;
using TaxRateCollector.Infrastructure.Data;
using TaxRateCollector.Infrastructure.Services;
using TaxRateCollector.Worker;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSerilog();

var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContextFactory<AppDbContext>(opts => opts.UseSqlServer(connStr));

var settings = new SettingsService();
settings.Load();
builder.Services.AddSingleton(settings);

builder.Services.AddHttpClient();
builder.Services.AddScoped<IDiscoveryService, DiscoveryService>();
builder.Services.Configure<AnthropicOptions>(builder.Configuration.GetSection(AnthropicOptions.Section));
builder.Services.AddLegionClient();
builder.Services.AddScoped<IRateLawExtractor, ClaudeRateLawExtractor>();
builder.Services.AddScoped<IRecursiveRateScraper, RecursiveRateScraper>();

builder.Services.AddHostedService<ScrapeJobWorker>();
builder.Services.AddHostedService<MonthlySchedulerService>();

var host = builder.Build();
await host.RunAsync();
