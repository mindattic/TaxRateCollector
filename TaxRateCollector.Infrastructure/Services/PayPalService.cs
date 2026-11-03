using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaxRateCollector.Core.Interfaces;
using TaxRateCollector.Infrastructure.Data;

namespace TaxRateCollector.Infrastructure.Services;

/// <summary>
/// PayPal Orders API v2 integration.
/// Operates in mock mode automatically when ClientId / ClientSecret are blank.
/// Flip to live by filling in credentials in Settings → Admin → PayPal.
/// </summary>
public class PayPalService(IDbContextFactory<AppDbContext> dbFactory, IHttpClientFactory httpFactory, ILogger<PayPalService> log) : IPayPalService
{
    public async Task<bool> IsConfiguredAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cfg = await db.PayPalConfigs.OrderBy(x => x.Id).FirstOrDefaultAsync();
        return cfg is { } c && !string.IsNullOrWhiteSpace(c.ClientId) && !string.IsNullOrWhiteSpace(c.ClientSecret);
    }

    public async Task<PayPalOrderResult> CreateOrderAsync(decimal amount, string currency, string description,
        string? returnUrl = null, string? cancelUrl = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cfg = await db.PayPalConfigs.OrderBy(x => x.Id).FirstOrDefaultAsync();

        if (cfg is null || string.IsNullOrWhiteSpace(cfg.ClientId) || string.IsNullOrWhiteSpace(cfg.ClientSecret))
        {
            var mockId = $"MOCK-{Guid.CreateVersion7():N}";
            log.LogInformation("PayPal mock mode — returning fake order {OrderId}", mockId);
            return new PayPalOrderResult(true, mockId, true, null, null);
        }

        try
        {
            var baseUrl = cfg.Mode == "live"
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";

            var token = await GetAccessTokenAsync(cfg.ClientId, cfg.ClientSecret, baseUrl);

            var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var purchaseUnits = new[]
            {
                new
                {
                    amount = new { currency_code = currency, value = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) },
                    description
                }
            };
            // Without a return URL PayPal has nowhere to send the buyer after approval.
            object payload = string.IsNullOrWhiteSpace(returnUrl)
                ? new { intent = "CAPTURE", purchase_units = purchaseUnits }
                : new
                {
                    intent = "CAPTURE",
                    purchase_units = purchaseUnits,
                    application_context = new
                    {
                        return_url  = returnUrl,
                        cancel_url  = string.IsNullOrWhiteSpace(cancelUrl) ? returnUrl : cancelUrl,
                        user_action = "PAY_NOW",
                        shipping_preference = "NO_SHIPPING",
                    }
                };

            var resp = await http.PostAsJsonAsync($"{baseUrl}/v2/checkout/orders", payload);
            resp.EnsureSuccessStatusCode();

            var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>();
            var root = doc!.RootElement;
            var orderId = root.GetProperty("id").GetString()!;
            string? approvalUrl = null;
            if (root.TryGetProperty("links", out var links))
                foreach (var l in links.EnumerateArray())
                {
                    var rel = l.TryGetProperty("rel", out var r) ? r.GetString() : null;
                    if (rel is "approve" or "payer-action" && l.TryGetProperty("href", out var href))
                    {
                        approvalUrl = href.GetString();
                        break;
                    }
                }

            if (string.IsNullOrWhiteSpace(approvalUrl))
                return new PayPalOrderResult(false, orderId, false, null, "PayPal returned no approval link for the order.");

            return new PayPalOrderResult(true, orderId, false, approvalUrl, null);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "PayPal CreateOrder failed");
            return new PayPalOrderResult(false, "", false, null, ex.Message);
        }
    }

    public async Task<bool> CaptureOrderAsync(string orderId)
    {
        if (orderId.StartsWith("MOCK-", StringComparison.Ordinal))
        {
            log.LogInformation("PayPal mock capture — order {OrderId} approved", orderId);
            return true;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var cfg = await db.PayPalConfigs.OrderBy(x => x.Id).FirstOrDefaultAsync();
        if (cfg is null) return false;

        try
        {
            var baseUrl = cfg.Mode == "live"
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";

            var token = await GetAccessTokenAsync(cfg.ClientId, cfg.ClientSecret, baseUrl);

            var http = httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await http.PostAsync($"{baseUrl}/v2/checkout/orders/{orderId}/capture",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("PayPal capture of {OrderId} returned {Status}", orderId, (int)resp.StatusCode);
                return false;
            }

            // A 2xx alone is not payment: only a COMPLETED order has moved the money.
            using var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>();
            var status = doc?.RootElement.TryGetProperty("status", out var s) == true ? s.GetString() : null;
            if (!string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                log.LogWarning("PayPal capture of {OrderId} ended in status {Status}", orderId, status);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "PayPal CaptureOrder failed for {OrderId}", orderId);
            return false;
        }
    }

    private async Task<string> GetAccessTokenAsync(string clientId, string clientSecret, string baseUrl)
    {
        var http = httpFactory.CreateClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("grant_type", "client_credentials")]);
        var resp = await http.PostAsync($"{baseUrl}/v1/oauth2/token", content);
        resp.EnsureSuccessStatusCode();

        var doc = await resp.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement.GetProperty("access_token").GetString()!;
    }
}
