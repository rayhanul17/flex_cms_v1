using FlexCms.Framework.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlexCms.Host.Controllers;

/// <summary>
/// Anonymous webhook receiver for payment gateways. Each gateway POSTs to its
/// own route (<c>/payment/webhook/{gatewayId}</c>) with a form-encoded or
/// JSON body. The dispatcher hands the payload to the matching gateway impl,
/// which validates the signature and returns a <see cref="PaymentResult"/>.
///
/// <para>
/// <b>Security</b>: gateway-side signature verification is the trust
/// boundary — never accept a result without one. Gateways that haven't
/// implemented their verification yet (bKash, Nagad — see their stub
/// notes) deliberately reject all webhooks until the integration is
/// completed with real merchant credentials.
/// </para>
/// </summary>
[Route("payment/webhook")]
[AllowAnonymous]
public class PaymentWebhookController : Controller
{
    private readonly DispatchingPaymentGateway _gateway;

    public PaymentWebhookController(DispatchingPaymentGateway gateway) => _gateway = gateway;

    [HttpPost("{gatewayId}")]
    public async Task<IActionResult> Receive(string gatewayId, CancellationToken ct)
    {
        // Accept either form-encoded or JSON — gateways differ. Read once into
        // a string→string dict so the gateway impl doesn't have to know which.
        var payload = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (Request.HasFormContentType)
        {
            foreach (var kv in await Request.ReadFormAsync(ct))
                payload[kv.Key] = kv.Value.ToString();
        }
        else
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync(ct);
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrEmpty(body) ? "{}" : body);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                        payload[prop.Name] = prop.Value.ToString();
                }
            }
            catch { /* malformed body → empty payload → gateway will reject */ }
        }

        var result = await _gateway.HandleWebhookAsync(gatewayId, payload, ct);
        return result.Success
            ? Ok(new { ok = true, transactionId = result.TransactionId, amount = result.Amount, status = result.Status })
            : BadRequest(new { ok = false, error = result.Error });
    }
}
