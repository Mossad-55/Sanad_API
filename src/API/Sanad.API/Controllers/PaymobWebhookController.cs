using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Infrastructure.Payments;

namespace Sanad.API.Controllers;

[ApiController]
[Route("api/v1/payments/webhooks")]
public sealed class PaymobWebhookController : ControllerBase
{
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

        string expected = PaymobHmacCalculator.Calculate(obj, _paymobOptions.HmacSecret);

        bool signatureValid;

        try
        {
            signatureValid = hmac is not null
                && CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expected),
                    Convert.FromHexString(hmac.Trim()));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            signatureValid = false;
        }

        if (!signatureValid)
        {
            return Unauthorized();
        }

        bool isRefundCallback =
            obj.TryGetProperty("is_refunded", out JsonElement isRefunded)
                && isRefunded.GetBoolean()
            || obj.TryGetProperty("has_parent_transaction", out JsonElement hasParent)
                && hasParent.GetBoolean();

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
            obj.GetProperty("success").GetBoolean(),
            obj.TryGetProperty("pending", out JsonElement pending) && pending.GetBoolean(),
            DateTime.UtcNow);

        var result = await _sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.Error.Code == "Paymob.AmountMismatch")
            {
                return BadRequest();
            }

            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }
}