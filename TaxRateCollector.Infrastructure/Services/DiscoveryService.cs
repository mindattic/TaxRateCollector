using System.Text.Json;
using Microsoft.Extensions.Logging;
using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Services;

public class DiscoveryService : IDiscoveryService
{
    private readonly HttpClient http;
    private readonly SettingsService settings;
    private readonly ILogger<DiscoveryService> logger;

    public DiscoveryService(HttpClient http, SettingsService settings, ILogger<DiscoveryService> logger)
    {
        this.http     = http;
        this.settings = settings;
        this.logger   = logger;
    }

    public async Task<DiscoveryResult> DiscoverAsync(Jurisdiction jurisdiction, CancellationToken ct = default)
    {
        var result = new DiscoveryResult
        {
            JurisdictionId   = jurisdiction.Id,
            JurisdictionName = jurisdiction.JurisdictionName,
            StateCode        = jurisdiction.StateCode,
            Tier             = jurisdiction.JurisdictionType,
            ProcessedAt      = DateTime.UtcNow,
        };

        if (string.IsNullOrWhiteSpace(jurisdiction.SourceUrl))
        {
            result.Status     = "Skipped";
            result.SourceUsed = "—";
            result.Notes      = "No source URL configured — add a SourceUrl to enable discovery";
            return result;
        }

        var url = jurisdiction.SourceUrl;

        if (await IsReachableAsync(url, ct))
        {
            result.Status     = "Found";
            result.SourceUsed = url;
            return result;
        }

        if (settings.Current.WaybackMachineFallback)
        {
            var archiveUrl = await GetWaybackUrlAsync(url, ct);
            if (archiveUrl is not null)
            {
                result.Status     = "WaybackFallback";
                result.SourceUsed = archiveUrl;
                result.Notes      = "Live URL unreachable; using Wayback Machine archive snapshot";
                return result;
            }
        }

        result.Status     = "NotFound";
        result.SourceUsed = url;
        result.Notes      = settings.Current.WaybackMachineFallback
            ? "URL unreachable and no archive snapshot available"
            : "URL unreachable";
        return result;
    }

    private async Task<bool> IsReachableAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var response = await http.SendAsync(
                new HttpRequestMessage(HttpMethod.Head, url), cts.Token);
            var code = (int)response.StatusCode;
            return code >= 200 && code < 400;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "HEAD request failed for {Url}", url);
            return false;
        }
    }

    private async Task<string?> GetWaybackUrlAsync(string url, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(url);
            var apiUrl  = $"https://archive.org/wayback/available?url={encoded}";
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            var json = await http.GetStringAsync(apiUrl, cts.Token);
            using var doc = JsonDocument.Parse(json);
            var snapshots = doc.RootElement.GetProperty("archived_snapshots");
            if (snapshots.TryGetProperty("closest", out var closest) &&
                closest.TryGetProperty("available", out var avail) &&
                avail.GetBoolean())
            {
                return closest.GetProperty("url").GetString();
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Wayback Machine lookup failed for {Url}", url);
        }
        return null;
    }
}
