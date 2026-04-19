using TaxRateCollector.Core.Entities;
using TaxRateCollector.Core.Interfaces;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// Stub implementation of <see cref="IRateLawExtractor"/>.
/// Always returns an empty list — replace this with the real AI-backed extractor
/// once Anthropic/Claude API integration is wired in.
/// </summary>
public sealed class StubRateLawExtractor : IRateLawExtractor
{
    public Task<IReadOnlyList<ExtractedRateLaw>> ExtractAsync(
        Jurisdiction jurisdiction,
        string content,
        string mimeType,
        string sourceUrl,
        CancellationToken ct = default)
    {
        IReadOnlyList<ExtractedRateLaw> empty = [];
        return Task.FromResult(empty);
    }
}
