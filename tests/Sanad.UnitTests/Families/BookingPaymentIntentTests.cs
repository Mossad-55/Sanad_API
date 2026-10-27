using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.UnitTests.Finance;
using Sanad.UnitTests.Support;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class BookingPaymentIntentTests
{
    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    private static (FamiliesDbContext DbContext, Family Family, Elderly Elderly) SeedFamily()
    {
        var dbContext = CreateDbContext();
        var family = Family.Create(UserId.New(), "Intent Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        dbContext.Families.Add(family);
        dbContext.Elderlies.Add(elderly);
        dbContext.SaveChanges();

        return (dbContext, family, elderly);
    }

    private sealed class FakePricing(BookingCaregiverType type, decimal fee) : ICaregiverBookingPricing
    {
        public Task<Result<CaregiverBookingPrice>> GetBookingPriceAsync(
            CaregiverId caregiverId,
            BookingShiftType shiftType,
            TimeOnly startTime,
            TimeOnly endTime,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<CaregiverBookingPrice>.Success(
                new CaregiverBookingPrice(type, fee)));
        }
    }

    private sealed class FakePaymobClient(PaymobPaymentIntent? intent, Error? error) : IPaymobClient
    {
        public int IntentCalls { get; private set; }

        public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
            PaymobPaymentIntentInput input,
            CancellationToken cancellationToken = default)
        {
            IntentCalls++;

            if (error is not null)
            {
                return Task.FromResult(Result<PaymobPaymentIntent>.Failure(error));
            }

            return Task.FromResult(Result<PaymobPaymentIntent>.Success(intent!));
        }

        public Task<Result<string?>> RefundPaymentAsync(
            string paymobTransactionId,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<string?>.Success($"dev-refund-{Guid.NewGuid():N}"));
        }

        public Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
            string refundTransactionId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<PaymobRefundVerification>.Failure(new Error(
                "Paymob.VerificationUnavailable",
                "Refund verification is not used by this test.")));
        }
    }

    private static async Task<(BookingCheckoutResponse Response, Booking Booking)> CheckoutAsync(
        FamiliesDbContext dbContext,
        Family family,
        Elderly elderly,
        FixedPlatformControls? controls = null)
    {
        var checkout = new CreateBookingCheckoutCommandHandler(
            dbContext,
            new FakePricing(BookingCaregiverType.Medical, 500m),
            new FixedPlatformChargeRules(15m, 0m),
            // E04: the control record is supplied explicitly; the seeded
            // production state is Operational with both methods enabled.
            controls ?? FixedPlatformControls.Operational(),
            new StubIdempotentResponseSnapshotter(),
            new StubCaregiverAvailabilityRules(),
            new PassThroughCaregiverBookingCheckoutGuard());

        var checkoutResult = await checkout.Handle(
            new CreateBookingCheckoutCommand(
                family.OwnerUserId,
                elderly.Id,
                CaregiverId.New(),
                BookingShiftType.HomeVisit,
                DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
                new TimeOnly(10, 0),
                new TimeOnly(12, 0),
                "123 Nile St, Cairo",
                null,
                DateOnly.FromDateTime(DateTime.UtcNow),
                DateTime.UtcNow),
            CancellationToken.None);

        Assert.True(checkoutResult.IsSuccess);

        Booking booking = dbContext.Bookings.Single();

        return (checkoutResult.Value.Value, booking);
    }

    private static CreateBookingPaymentIntentCommandHandler IntentHandler(
        FamiliesDbContext dbContext,
        IPaymobClient paymob,
        FixedPlatformControls? controls = null)
    {
        // E04: one explicit in-memory control record supplies both the control
        // state and the method-configuration truth.
        FixedPlatformControls platform = controls ?? FixedPlatformControls.Operational();

        return new CreateBookingPaymentIntentCommandHandler(dbContext, paymob, platform, platform);
    }

    private static CreateBookingPaymentIntentCommand IntentCommand(
        Family family,
        Booking booking,
        PaymentMethod method)
    {
        return new CreateBookingPaymentIntentCommand(
            booking.Id,
            family.OwnerUserId,
            method,
            ValidBilling(),
            DateTime.UtcNow);
    }

    private static PaymobBillingData ValidBilling()
    {
        return new PaymobBillingData(
            "Ahmed",
            "Ali",
            "ahmed@example.com",
            "+201012345678");
    }

    [Fact]
    public async Task Intent_ShouldRecordTransaction_AndReturnSdkHandoff()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);

        // The dev client echoes the booking id as the Paymob order id —
        // the same contract as the real intention flow (special_reference).
        string expectedOrderId = booking.Id.Value.ToString();

        var handler = IntentHandler(
            dbContext,
            new FakePaymobClient(
                new PaymobPaymentIntent(expectedOrderId, "pi_test_1", "egy_csk_test_abc", "pk_test_abc"),
                null));

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Card),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedOrderId, result.Value.PaymobOrderId);
        Assert.Equal("egy_csk_test_abc", result.Value.ClientSecret);
        Assert.Equal("pk_test_abc", result.Value.PublicKey);
        Assert.Equal(575m, result.Value.Amount);
        Assert.Equal("EGP", result.Value.Currency);

        var transaction = Assert.Single(booking.PaymentTransactions);
        Assert.Equal(PaymentTransactionStatus.Pending, transaction.Status);
        Assert.Equal(PaymentMethod.Card, transaction.Method);
        Assert.Equal(expectedOrderId, transaction.PaymobOrderId);
        Assert.Equal(575m, transaction.Amount);
    }

    [Fact]
    public async Task Intent_ShouldReject_WhenBookingAlreadyPaid()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);

        booking.MarkAsPaid(booking.Id.Value.ToString(), "PM-TXN-1", DateTime.UtcNow);
        dbContext.SaveChanges();

        var handler = IntentHandler(
            dbContext,
            new FakePaymobClient(
                new PaymobPaymentIntent(
                    booking.Id.Value.ToString(),
                    "pi_test_2",
                    "egy_csk_test_abc",
                    "pk_test_abc"),
                null));

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Card),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.Domain.InvalidOperation", result.Error.Code);
    }

    [Fact]
    public async Task Intent_ShouldRejectForeignFamilyBooking()
    {
        var (dbContext, family, elderly) = SeedFamily();

        var attackerFamily = Family.Create(UserId.New(), "Other Family");
        dbContext.Families.Add(attackerFamily);
        dbContext.SaveChanges();

        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);

        var command = new CreateBookingPaymentIntentCommand(
            booking.Id,
            attackerFamily.OwnerUserId,
            PaymentMethod.Card,
            new PaymobBillingData("Ali", "Hassan", "ali@example.com", "+201098765432"),
            DateTime.UtcNow);

        var handler = IntentHandler(
            dbContext,
            new FakePaymobClient(
                new PaymobPaymentIntent(
                    booking.Id.Value.ToString(),
                    "pi_test_3",
                    "egy_csk_test_abc",
                    "pk_test_abc"),
                null));

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Bookings.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Intent_ShouldPropagateGatewayFailure_AndNotRecordTransaction()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);

        var handler = IntentHandler(
            dbContext,
            new FakePaymobClient(
                null,
                new Error("Paymob.MethodNotAvailable", "The selected payment method is not available.")));

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Wallet),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Paymob.MethodNotAvailable", result.Error.Code);
        Assert.Empty(dbContext.Bookings.Single().PaymentTransactions);
    }

    [Fact]
    public async Task Validator_ShouldAcceptAnyDefinedMethod()
    {
        var validator = new CreateBookingPaymentIntentCommandValidator();

        // Wallet no longer needs a number inside the command — the SDK collects it.
        var wallet = new CreateBookingPaymentIntentCommand(
            BookingId.New(),
            UserId.New(),
            PaymentMethod.Wallet,
            ValidBilling(),
            DateTime.UtcNow);

        Assert.True((await validator.ValidateAsync(wallet)).IsValid);

        var undefinedMethod = new CreateBookingPaymentIntentCommand(
            BookingId.New(),
            UserId.New(),
            (PaymentMethod)99,
            ValidBilling(),
            DateTime.UtcNow);

        Assert.False((await validator.ValidateAsync(undefinedMethod)).IsValid);

        var missingBilling = new CreateBookingPaymentIntentCommand(
            BookingId.New(),
            UserId.New(),
            PaymentMethod.Card,
            new PaymobBillingData("", "Ali", "not-an-email", "123"),
            DateTime.UtcNow);

        Assert.False((await validator.ValidateAsync(missingBilling)).IsValid);
    }

    // ---------------------- E04-B platform control gates ----------------------

    [Fact]
    public async Task Checkout_DuringMaintenance_IsRejectedWithoutCreatingABooking()
    {
        var (dbContext, family, elderly) = SeedFamily();

        var checkout = new CreateBookingCheckoutCommandHandler(
            dbContext,
            new FakePricing(BookingCaregiverType.Medical, 500m),
            new FixedPlatformChargeRules(15m, 0m),
            FixedPlatformControls.Maintenance(),
            new StubIdempotentResponseSnapshotter(),
            new StubCaregiverAvailabilityRules(),
            new PassThroughCaregiverBookingCheckoutGuard());

        var result = await checkout.Handle(
            CheckoutCommand(family, elderly),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.Maintenance", result.Error.Code);
        Assert.Empty(dbContext.Bookings);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unreadable")]
    public async Task Checkout_FailsClosed_WhenTheControlRecordIsUnavailable(string state)
    {
        var (dbContext, family, elderly) = SeedFamily();

        FixedPlatformControls controls = state == "missing"
            ? FixedPlatformControls.Missing()
            : FixedPlatformControls.Unreadable();

        var checkout = new CreateBookingCheckoutCommandHandler(
            dbContext,
            new FakePricing(BookingCaregiverType.Medical, 500m),
            new FixedPlatformChargeRules(15m, 0m),
            controls,
            new StubIdempotentResponseSnapshotter(),
            new StubCaregiverAvailabilityRules(),
            new PassThroughCaregiverBookingCheckoutGuard());

        var result = await checkout.Handle(
            CheckoutCommand(family, elderly),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.ControlStateUnavailable", result.Error.Code);
        Assert.Empty(dbContext.Bookings);
    }

    [Fact]
    public async Task Intent_DuringMaintenance_IsRejectedBeforeAnyProviderCall()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);
        var paymob = new FakePaymobClient(
            new PaymobPaymentIntent(booking.Id.Value.ToString(), "pi_m", "cs_m", "pk_m"),
            null);

        var handler = IntentHandler(dbContext, paymob, FixedPlatformControls.Maintenance());

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Card),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.Maintenance", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(booking.PaymentTransactions);
    }

    [Fact]
    public async Task Intent_ForADisabledMethod_IsRejectedBeforeAnyProviderCall()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);
        var paymob = new FakePaymobClient(
            new PaymobPaymentIntent(booking.Id.Value.ToString(), "pi_d", "cs_d", "pk_d"),
            null);

        var controls = new FixedPlatformControls { CardEnabled = false };
        var handler = IntentHandler(dbContext, paymob, controls);

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Card),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.PaymentMethodUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(dbContext.Bookings.Single().PaymentTransactions);

        // The still-enabled Wallet method stays usable.
        controls.CardEnabled = true;
        Assert.True((await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Wallet),
            CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task Intent_ForAnUnconfiguredMethod_IsRejectedBeforeAnyProviderCall()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);
        var paymob = new FakePaymobClient(
            new PaymobPaymentIntent(booking.Id.Value.ToString(), "pi_u", "cs_u", "pk_u"),
            null);

        var controls = new FixedPlatformControls { WalletConfigured = false };
        var handler = IntentHandler(dbContext, paymob, controls);

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Wallet),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.PaymentMethodUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(booking.PaymentTransactions);
    }

    [Fact]
    public async Task ApplePay_IsRejectedAsUnavailable_WithoutCallingTheProvider()
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);
        var paymob = new FakePaymobClient(
            new PaymobPaymentIntent(booking.Id.Value.ToString(), "pi_ap", "cs_ap", "pk_ap"),
            null);

        var handler = IntentHandler(dbContext, paymob);

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.ApplePay),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.PaymentMethodUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(booking.PaymentTransactions);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unreadable")]
    public async Task Intent_FailsClosed_WhenTheControlRecordIsUnavailable(string state)
    {
        var (dbContext, family, elderly) = SeedFamily();
        var (_, booking) = await CheckoutAsync(dbContext, family, elderly);
        var paymob = new FakePaymobClient(
            new PaymobPaymentIntent(booking.Id.Value.ToString(), "pi_x", "cs_x", "pk_x"),
            null);

        FixedPlatformControls controls = state == "missing"
            ? FixedPlatformControls.Missing()
            : FixedPlatformControls.Unreadable();

        var handler = IntentHandler(dbContext, paymob, controls);

        var result = await handler.Handle(
            IntentCommand(family, booking, PaymentMethod.Card),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Platform.ControlStateUnavailable", result.Error.Code);
        Assert.Equal(0, paymob.IntentCalls);
        Assert.Empty(booking.PaymentTransactions);
    }

    private static CreateBookingCheckoutCommand CheckoutCommand(Family family, Elderly elderly) =>
        new(
            family.OwnerUserId,
            elderly.Id,
            CaregiverId.New(),
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTime.UtcNow);
}

/// <summary>
/// E04-B test double: one explicit in-memory platform control record that also
/// answers the payment-method configuration question. Tests always state the
/// platform state and configuration they expect; there is no implicit
/// Operational fallback in the gated handlers themselves.
/// </summary>
internal sealed class FixedPlatformControls : IPlatformControlReader, IPaymentMethodConfiguration
{
    private static readonly Guid ControlId = Guid.Parse("0198e2c2-4444-7777-8888-0000000000e4");

    /// <summary>Operational with both methods enabled and configured and the
    /// subscription-enrollment integration configured, matching the seeded
    /// production state.</summary>
    public static FixedPlatformControls Operational() => new();

    public static FixedPlatformControls Maintenance() =>
        new() { State = PlatformState.Maintenance };

    /// <summary>No control record at all: gated operations must fail closed.</summary>
    public static FixedPlatformControls Missing() => new() { RecordMissing = true };

    /// <summary>The control record exists but cannot be read.</summary>
    public static FixedPlatformControls Unreadable() => new() { ReadFails = true };

    public PlatformState State { get; set; } = PlatformState.Operational;

    public bool CardEnabled { get; set; } = true;

    public bool WalletEnabled { get; set; } = true;

    public bool CardConfigured { get; set; } = true;

    public bool WalletConfigured { get; set; } = true;

    /// <summary>
    /// Truth for the Card subscription-enrollment branch only (a Card initial
    /// subscription payment for a plan with a provider subscription plan id).
    /// It never affects the generic Card flows or the public method list, which
    /// stay driven by <see cref="CardConfigured"/>.
    /// </summary>
    public bool CardSubscriptionEnrollmentConfigured { get; set; } = true;

    public bool RecordMissing { get; set; }

    public bool ReadFails { get; set; }

    public Task<PlatformControlSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        if (ReadFails)
        {
            return Task.FromException<PlatformControlSnapshot?>(
                new InvalidOperationException("control record unavailable"));
        }

        if (RecordMissing)
        {
            return Task.FromResult<PlatformControlSnapshot?>(null);
        }

        return Task.FromResult<PlatformControlSnapshot?>(
            new PlatformControlSnapshot(
                ControlId,
                new PlatformControlValues(State, null, null, CardEnabled, WalletEnabled),
                1,
                new DateTime(2026, 10, 19, 6, 16, 22, DateTimeKind.Utc)));
    }
}
