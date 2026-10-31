using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.Families.Application.Bookings;
using Ahlena.Modules.Families.Application.Subscriptions;
using Ahlena.Modules.Families.Infrastructure.Payments;

namespace Ahlena.UnitTests.API;

/// <summary>
/// Owner decision D5: the Rentals runtime feature is removed while the deployed
/// schema and history are retained, so a verified legacy <c>rnt_</c> Paymob
/// callback can still arrive with no handler left. These tests pin the bounded
/// disposition implemented on the shared webhook controller: HMAC validation
/// still governs the branch, the verified event is never dispatched to any
/// booking, subscription or care-home handler, it is never acknowledged as a
/// completed financial settlement, and it stays visible to the provider and to
/// operations through an explicit non-2xx "requires reconciliation" response.
/// </summary>
public sealed class LegacyRentalWebhookCallbackTests
{
    private const string Secret = "test-secret";
    private const string LegacyRentalOrderId = "rnt_0198e2c2-7777-7777-8888-000000000007";
    private const string ReconciliationTitle = "Legacy rental payment callback requires reconciliation";

    [Fact]
    public async Task Verified_legacy_rental_callback_is_not_dispatched_and_requires_reconciliation()
    {
        var sender = new RecordingSender(BookingSuccess());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null,
            SignedTransaction(Secret, LegacyRentalOrderId),
            default);

        // Never acknowledged as a settled payment.
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        ProblemDetails problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(ReconciliationTitle, problem.Title);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.Status);

        // No handler of any kind ran: not the family-booking handler, not the
        // subscription handler, not a care-home handler.
        Assert.Empty(sender.Requests);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task Legacy_rental_callback_signed_with_the_wrong_secret_is_rejected()
    {
        var sender = new RecordingSender(BookingSuccess());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null,
            SignedTransaction("wrong-secret", LegacyRentalOrderId),
            default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task Legacy_rental_callback_without_any_hmac_is_rejected()
    {
        var sender = new RecordingSender(BookingSuccess());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null,
            UnsignedTransaction(LegacyRentalOrderId),
            default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task Legacy_rental_callback_without_a_configured_secret_is_not_dispatched()
    {
        var sender = new RecordingSender(BookingSuccess());
        PaymobWebhookController controller = CreateController(sender, string.Empty);

        IActionResult result = await controller.HandlePaymob(
            null,
            SignedTransaction(Secret, LegacyRentalOrderId),
            default);

        // The unconfigured-secret guard is a bare status result, deliberately
        // distinct from the reconciliation Problem above.
        Assert.Equal(
            StatusCodes.Status503ServiceUnavailable,
            Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task Non_rental_reference_after_removal_still_reaches_the_booking_handler()
    {
        var sender = new RecordingSender(BookingSuccess());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null,
            SignedTransaction(Secret, "bk_0198e2c2-7777-7777-8888-0000000000ff"),
            default);

        Assert.IsType<OkResult>(result);
        ConfirmBookingPaymentCommand command =
            Assert.IsType<ConfirmBookingPaymentCommand>(sender.LastRequest);
        Assert.Equal("bk_0198e2c2-7777-7777-8888-0000000000ff", command.PaymobOrderId);
    }

    [Fact]
    public async Task Care_home_reference_after_removal_still_reaches_the_care_home_handler()
    {
        var sender = new RecordingSender(Result.Success());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null,
            SignedTransaction(Secret, "chb_0198e2c2-7777-7777-8888-0000000000aa"),
            default);

        Assert.IsType<OkResult>(result);
        ConfirmCareHomePaymentCommand command =
            Assert.IsType<ConfirmCareHomePaymentCommand>(sender.LastRequest);
        Assert.Equal("chb_0198e2c2-7777-7777-8888-0000000000aa", command.MerchantReference);
    }

    [Fact]
    public async Task Subscription_reference_after_removal_still_reaches_the_subscription_handler()
    {
        var sender = new RecordingSender(SubscriptionSuccess());
        PaymobWebhookController controller = CreateController(sender, Secret);

        IActionResult result = await controller.HandlePaymob(
            null,
            SignedTransaction(Secret, "sub_0198e2c2-7777-7777-8888-0000000000bb"),
            default);

        Assert.IsType<OkResult>(result);
        ConfirmSubscriptionPaymentCommand command =
            Assert.IsType<ConfirmSubscriptionPaymentCommand>(sender.LastRequest);
        Assert.Equal("sub_0198e2c2-7777-7777-8888-0000000000bb", command.MerchantReference);
    }

    private static Result<ConfirmBookingPaymentResponse> BookingSuccess() =>
        Result<ConfirmBookingPaymentResponse>.Success(
            new ConfirmBookingPaymentResponse(Guid.NewGuid(), "Paid"));

    private static Result<ConfirmSubscriptionPaymentResponse> SubscriptionSuccess() =>
        Result<ConfirmSubscriptionPaymentResponse>.Success(
            new ConfirmSubscriptionPaymentResponse(Guid.NewGuid(), "Paid"));

    /// <summary>
    /// A Paymob transaction payload whose HMAC is computed with the documented
    /// field order, exactly as the provider signs it. The merchant order id is
    /// the only part that varies between the retained order families (booking,
    /// subscription, care home) and the removed legacy rental family.
    /// </summary>
    private static JsonElement SignedTransaction(string secret, string merchantOrderId)
    {
        string obj = TransactionObject(merchantOrderId);
        using JsonDocument unsigned = JsonDocument.Parse(obj);
        string hmac = PaymobHmacCalculator.Calculate(unsigned.RootElement, secret);
        using JsonDocument signed = JsonDocument.Parse($"{{\"obj\":{obj},\"hmac\":\"{hmac}\"}}");
        return signed.RootElement.Clone();
    }

    private static JsonElement UnsignedTransaction(string merchantOrderId)
    {
        using JsonDocument document = JsonDocument.Parse(
            $"{{\"obj\":{TransactionObject(merchantOrderId)}}}");
        return document.RootElement.Clone();
    }

    private static string TransactionObject(string merchantOrderId) =>
        "{\"id\":9001,\"amount_cents\":45000,\"success\":true,\"pending\":false,\"currency\":\"EGP\","
        + $"\"order\":{{\"id\":7001,\"merchant_order_id\":\"{merchantOrderId}\"}}}}";

    private static PaymobWebhookController CreateController(RecordingSender sender, string secret) =>
        new(sender, Options.Create(new PaymobOptions { HmacSecret = secret }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    /// <summary>
    /// Records every dispatch so a test can prove that no handler was reached,
    /// not merely that the last one differs.
    /// </summary>
    private sealed class RecordingSender(object response) : ISender
    {
        public List<object> Requests { get; } = [];

        public object? LastRequest => Requests.Count == 0 ? null : Requests[^1];

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
