using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Enums;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Phase 1 stub — walks every jurisdiction and reports what source channels are available.
/// Phase 2 will implement actual discovery via web scraping, PDF extraction, API calls,
/// and news article parsing depending on what sources are configured per jurisdiction.
/// </summary>
public class DiscoveryService : IDiscoveryService
{
    private readonly HttpClient http;

    public DiscoveryService(HttpClient http)
    {
        this.http = http;
    }

    public async Task<DiscoveryResult> DiscoverAsync(Jurisdiction jurisdiction, CancellationToken ct = default)
    {
        // Simulate a small amount of async work per jurisdiction
        await Task.Delay(20, ct);

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
            result.Status    = "Skipped";
            result.SourceUsed = "—";
            result.Notes     = "No source URL configured — add a SourceUrl to enable discovery";
            return result;
        }

        // Phase 2: choose strategy based on source URL / content type
        // Possible strategies (to be wired in Phase 2):
        //   • HTML scraper   — parse a tax rate table from the jurisdiction's website
        //   • PDF extractor  — download and OCR/parse a PDF rate schedule
        //   • REST API       — call a government data API endpoint
        //   • News feed      — extract rate changes mentioned in press releases
        var scheme = DetermineSourceType(jurisdiction.SourceUrl);
        result.SourceUsed = jurisdiction.SourceUrl;
        result.Status     = "NotFound";
        result.Notes      = $"Phase 2 stub — source type detected: {scheme}. Discovery not yet implemented for this channel.";

        return result;
    }

    private static string DetermineSourceType(string url)
    {
        var lower = url.ToLowerInvariant();
        if (lower.EndsWith(".pdf"))    return "PDF";
        if (lower.Contains("/api/"))   return "REST API";
        if (lower.Contains("news") || lower.Contains("press")) return "News/Press";
        return "Web Page";
    }
}
