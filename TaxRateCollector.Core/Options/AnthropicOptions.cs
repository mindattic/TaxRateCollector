namespace TaxRateCollector.Core.Options;

public class AnthropicOptions
{
    public const string Section = "Anthropic";

    /// <summary>Model ID to use for rate law extraction. Defaults to claude-sonnet-4-6.</summary>
    public string Model { get; set; } = "claude-sonnet-5";

    /// <summary>Max tokens to request in the extraction response.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Max characters of source content sent to Claude per request.</summary>
    public int MaxContentChars { get; set; } = 60_000;
}
