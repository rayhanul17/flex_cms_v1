using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FlexCms.Framework.Db;
using Microsoft.Extensions.Logging;

namespace FlexCms.Framework.Webhooks;

public sealed class WebhookDispatcher : IWebhookDispatcher
{
    public const int MaxAttempts = 3;
    public const string SignatureHeader = "X-Fcms-Signature";
    public const string EventHeader = "X-Fcms-Event";

    private readonly IRepository<FcmsWebhookEndpoint> _endpoints;
    private readonly IRepository<FcmsWebhookDelivery> _deliveries;
    private readonly IFcmsUnitOfWork _uow;
    private readonly HttpClient _http;
    private readonly ILogger<WebhookDispatcher> _logger;

    public WebhookDispatcher(
        IRepository<FcmsWebhookEndpoint> endpoints,
        IRepository<FcmsWebhookDelivery> deliveries,
        IFcmsUnitOfWork uow,
        HttpClient http,
        ILogger<WebhookDispatcher> logger)
    {
        _endpoints = endpoints;
        _deliveries = deliveries;
        _uow = uow;
        _http = http;
        _logger = logger;
    }

    public async Task<int> FireAsync(string eventName, object payload, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventName)) return 0;

        var json = JsonSerializer.Serialize(payload);
        var allEndpoints = await _endpoints.FindAsync(e => e.IsActive, ct);
        var matching = allEndpoints
            .Where(e => SubscribedTo(e.Events, eventName))
            .ToList();
        if (matching.Count == 0) return 0;

        foreach (var endpoint in matching)
        {
            var delivery = new FcmsWebhookDelivery
            {
                EndpointId = endpoint.Id,
                EventName = eventName,
                PayloadJson = json
            };
            await _deliveries.AddAsync(delivery, ct);

            await AttemptAsync(endpoint, delivery, ct);
        }

        await _uow.SaveChangesAsync(ct);
        return matching.Count;
    }

    public async Task RetryFailedAsync(CancellationToken ct = default)
    {
        var pending = await _deliveries.FindAsync(
            d => d.DeliveryStatus == WebhookDeliveryStatus.Pending && d.AttemptCount < MaxAttempts, ct);
        if (pending.Count == 0) return;

        // Look up endpoints in one shot to avoid N queries.
        var endpointIds = pending.Select(d => d.EndpointId).Distinct().ToList();
        var endpoints = (await _endpoints.GetByIdsAsync(endpointIds, ct))
            .ToDictionary(e => e.Id);

        foreach (var d in pending)
        {
            if (!endpoints.TryGetValue(d.EndpointId, out var endpoint) || !endpoint.IsActive)
            {
                d.DeliveryStatus = WebhookDeliveryStatus.Failed;
                d.LastError = "Endpoint not found or inactive.";
                d.CompletedAt = Clock.FcmsTime.Now;
                await _deliveries.UpdateAsync(d, ct);
                continue;
            }
            await AttemptAsync(endpoint, d, ct);
        }

        await _uow.SaveChangesAsync(ct);
    }

    private async Task AttemptAsync(FcmsWebhookEndpoint endpoint, FcmsWebhookDelivery delivery, CancellationToken ct)
    {
        delivery.AttemptCount++;
        delivery.LastAttemptAt = Clock.FcmsTime.Now;

        try
        {
            using var content = new StringContent(delivery.PayloadJson, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint.Url) { Content = content };
            req.Headers.Add(EventHeader, delivery.EventName);
            req.Headers.Add(SignatureHeader, ComputeSignature(endpoint.Secret, delivery.PayloadJson));

            using var resp = await _http.SendAsync(req, ct);
            delivery.LastResponseStatus = (int)resp.StatusCode;
            delivery.LastResponseBody = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                delivery.DeliveryStatus = WebhookDeliveryStatus.Succeeded;
                delivery.CompletedAt = Clock.FcmsTime.Now;
                delivery.LastError = null;
            }
            else
            {
                delivery.LastError = $"HTTP {(int)resp.StatusCode}";
                if (delivery.AttemptCount >= MaxAttempts)
                {
                    delivery.DeliveryStatus = WebhookDeliveryStatus.Failed;
                    delivery.CompletedAt = Clock.FcmsTime.Now;
                }
            }
        }
        catch (Exception ex)
        {
            delivery.LastError = ex.Message;
            if (delivery.AttemptCount >= MaxAttempts)
            {
                delivery.DeliveryStatus = WebhookDeliveryStatus.Failed;
                delivery.CompletedAt = Clock.FcmsTime.Now;
            }
            _logger.LogWarning(ex, "Webhook attempt {Attempt}/{Max} to {Url} threw", delivery.AttemptCount, MaxAttempts, endpoint.Url);
        }

        await _deliveries.UpdateAsync(delivery, ct);
    }

    /// <summary>HMAC-SHA256 of the body, hex-lowercased — same format GitHub/Stripe use.</summary>
    public static string ComputeSignature(string secret, string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret ?? ""));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body ?? ""));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool SubscribedTo(string events, string eventName)
        => (events ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(e => string.Equals(e, eventName, StringComparison.OrdinalIgnoreCase) || e == "*");
}
