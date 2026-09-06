using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Infrastructure.Payments;

namespace Sanad.API.Controllers;

[AllowAnonymous]
[ApiController]
[Route("api/v1/payments/webhooks")]
public sealed class PaymobWebhookController : ControllerBase
{
    private const string HmacHeaderName = "X-Paymob-Hmac";

    private readonly ISender _sender;
    private readonly PaymobOptions _paymobOptions;

    public PaymobWebhookController(
        ISender sender,
        IOptions<PaymobOptions> paymobOptions)
    {
        _sender = sender;
        _paymobOptions = paymobOptions.Value;
    }

    [HttpPost("paymob")]
    public async Task<IActionResult> HandlePaymob(
        [FromQuery(Name = "hmac")] string? hmac,
        [FromBody] JsonElement body,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_paymobOptions.HmacSecret))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        if (!body.TryGetProperty("obj", out JsonElement obj)
            || obj.ValueKind is not JsonValueKind.Object)
        {
            return BadRequest();
        }

        Request.Headers.TryGetValue(HmacHeaderName, out var headerHmac);

        string? providedHmac = PaymobHmacCalculator.CoalesceProvidedHmac(
            hmac,
            body,
            headerHmac.ToString());

        if (!PaymobHmacCalculator.IsValid(obj, _paymobOptions.HmacSecret, providedHmac))
        {
            return Unauthorized();
        }

        bool isRefundCallback =
            obj.TryGetProperty("is_refunded", out JsonElement isRefunded)
                && isRefunded.ValueKind is JsonValueKind.True
            || obj.TryGetProperty("has_parent_transaction", out JsonElement hasParent)
                && hasParent.ValueKind is JsonValueKind.True;

        if (isRefundCallback
            || !obj.TryGetProperty("order", out JsonElement order)
            || !order.TryGetProperty("merchant_order_id", out JsonElement merchantOrder))
        {
            return Ok();
        }

        var command = new ConfirmBookingPaymentCommand(
            merchantOrder.GetString() ?? string.Empty,
            obj.GetProperty("id").GetInt64(),
            obj.GetProperty("amount_cents").GetInt64(),
            obj.TryGetProperty("success", out JsonElement success) && success.ValueKind is JsonValueKind.True,
            obj.TryGetProperty("pending", out JsonElement pending) && pending.ValueKind is JsonValueKind.True,
            DateTime.UtcNow);

        var result = await _sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.Error.Code is "Paymob.AmountMismatch")
            {
                return BadRequest();
            }

            // Unknown order after a valid HMAC: acknowledge so Paymob stops retrying.
            if (result.Error.Code is "Bookings.NotFound")
            {
                return Ok();
            }

            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }
}
