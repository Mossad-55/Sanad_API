using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Application.Subscriptions;
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

        if (body.TryGetProperty("subscription_data", out JsonElement subscriptionData)
            && subscriptionData.ValueKind is JsonValueKind.Object
            && body.TryGetProperty("trigger_type", out JsonElement triggerTypeElement)
            && triggerTypeElement.ValueKind is JsonValueKind.String)
        {
            return await HandleSubscriptionCallback(
                body,
                subscriptionData,
                triggerTypeElement.GetString() ?? string.Empty,
                cancellationToken);
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

        string merchantReference = merchantOrder.GetString() ?? string.Empty;
        long transactionId = obj.GetProperty("id").GetInt64();
        long amountCents = obj.GetProperty("amount_cents").GetInt64();
        bool success = obj.TryGetProperty("success", out JsonElement successElement)
            && successElement.ValueKind is JsonValueKind.True;
        bool pending = obj.TryGetProperty("pending", out JsonElement pendingElement)
            && pendingElement.ValueKind is JsonValueKind.True;
        string currency = obj.TryGetProperty("currency", out JsonElement currencyElement)
            ? currencyElement.GetString() ?? string.Empty
            : string.Empty;

        if (merchantReference.StartsWith("sub_", StringComparison.Ordinal))
        {
            var subscriptionResult = await _sender.Send(
                new ConfirmSubscriptionPaymentCommand(
                    merchantReference,
                    transactionId,
                    amountCents,
                    currency,
                    success,
                    pending,
                    DateTime.UtcNow),
                cancellationToken);

            if (!subscriptionResult.IsSuccess)
            {
                if (subscriptionResult.Error.Code is "Paymob.AmountMismatch")
                    return BadRequest();
                if (subscriptionResult.Error.Code is "Subscriptions.Payment.NotFound"
                    or "Subscriptions.Payment.CurrentExists")
                    return Ok();
                return StatusCode(StatusCodes.Status500InternalServerError);
            }

            return Ok();
        }

        var command = new ConfirmBookingPaymentCommand(
            merchantReference,
            transactionId,
            amountCents,
            success,
            pending,
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

    private async Task<IActionResult> HandleSubscriptionCallback(
        JsonElement body,
        JsonElement subscriptionData,
        string triggerType,
        CancellationToken cancellationToken)
    {
        if (!TryGetScalarText(subscriptionData, "id", out string providerSubscriptionId))
            return BadRequest();

        if (!body.TryGetProperty("hmac", out JsonElement hmacElement)
            || hmacElement.ValueKind != JsonValueKind.String
            || !PaymobHmacCalculator.FixedTimeHexEquals(
                CalculateSubscriptionHmac(triggerType, providerSubscriptionId, _paymobOptions.HmacSecret),
                hmacElement.GetString() ?? string.Empty))
            return Unauthorized();

        if (!IsKnownSubscriptionTrigger(triggerType))
            return Ok();

        string? initialTransactionId = TryGetScalarText(subscriptionData, "initial_transaction", out string initial)
            ? initial
            : null;
        string? paymobRequestId = null;
        if (body.TryGetProperty("paymob_request_id", out JsonElement requestIdElement))
        {
            if (requestIdElement.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)
                || string.IsNullOrWhiteSpace(requestIdElement.GetRawText().Trim('"')))
                return BadRequest();
            paymobRequestId = requestIdElement.ValueKind == JsonValueKind.String
                ? requestIdElement.GetString()
                : requestIdElement.GetRawText();
        }
        long? amountCents = null;
        if (subscriptionData.TryGetProperty("amount_cents", out JsonElement amountElement))
        {
            if (amountElement.ValueKind != JsonValueKind.Number || !amountElement.TryGetInt64(out long amount))
                return BadRequest();
            amountCents = amount;
        }

        string? state = subscriptionData.TryGetProperty("state", out JsonElement stateElement)
            && stateElement.ValueKind == JsonValueKind.String
            ? stateElement.GetString()
            : null;
        DateTime? nextBillingOnUtc = null;
        if (subscriptionData.TryGetProperty("next_billing", out JsonElement nextBillingElement)
            && nextBillingElement.ValueKind != JsonValueKind.Null)
        {
            if (nextBillingElement.ValueKind != JsonValueKind.String
                || !DateTime.TryParse(
                    nextBillingElement.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTime parsedNextBilling))
                return BadRequest();
            nextBillingOnUtc = parsedNextBilling;
        }

        var result = await _sender.Send(
            new HandlePaymobSubscriptionCallbackCommand(
                triggerType,
                providerSubscriptionId,
                initialTransactionId,
                amountCents,
                state,
                nextBillingOnUtc,
                DateTime.UtcNow,
                paymobRequestId),
            cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.Error.Code == "Paymob.AmountMismatch")
                return BadRequest();
            if (result.Error.Code == "Subscriptions.Callback.ProviderEventIdRequired")
                return BadRequest();
            if (result.Error.Code == "Subscriptions.Payment.ProviderIdentityConflict"
                || result.Error.Code == "Subscriptions.Callback.ConcurrencyConflict")
                return Conflict();
            // The terminal grace-expired state is already recorded; acknowledge it so Paymob does not retry.
            if (result.Error.Code == "Subscriptions.Renewal.GraceExpired")
                return Ok();
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok();
    }

    private static string CalculateSubscriptionHmac(
        string triggerType,
        string providerSubscriptionId,
        string hmacSecret)
    {
        byte[] key = Encoding.UTF8.GetBytes(hmacSecret);
        byte[] data = Encoding.UTF8.GetBytes($"{triggerType}for{providerSubscriptionId}");
        return Convert.ToHexString(HMACSHA512.HashData(key, data)).ToLowerInvariant();
    }

    private static bool IsKnownSubscriptionTrigger(string triggerType) =>
        string.Equals(triggerType.Trim(), "CREATED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(triggerType.Trim(), "Subscription Created", StringComparison.OrdinalIgnoreCase)
        || string.Equals(triggerType.Trim(), "Successful Transaction", StringComparison.OrdinalIgnoreCase)
        || string.Equals(triggerType.Trim(), "Failed Transaction", StringComparison.OrdinalIgnoreCase)
        || string.Equals(triggerType.Trim(), "Failed Overdue Transaction", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetScalarText(JsonElement parent, string propertyName, out string value)
    {
        value = string.Empty;
        if (!parent.TryGetProperty(propertyName, out JsonElement element))
            return false;

        if (element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        if (element.ValueKind == JsonValueKind.Number)
        {
            value = element.GetRawText();
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }
}
