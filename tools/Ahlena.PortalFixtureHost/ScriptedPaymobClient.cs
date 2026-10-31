using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Families.Application.Abstractions.Payments;
using Ahlena.Modules.Identity.Application.Abstractions.Messaging;
using Ahlena.Modules.Identity.Domain.Authentication.VerificationRequests;
using Ahlena.Modules.Notifications.Application.Abstractions.Push;

namespace Ahlena.PortalFixtureHost;

/// <summary>
/// The explicit outcome the scripted payment double must produce for the next
/// refund call. Every value is synthetic: none of them is a provider movement,
/// and none of them is inferred from configuration or from the environment.
/// </summary>
public enum ScriptedRefundOutcome
{
    /// <summary>A locally fabricated refund reference. Proves nothing about a provider.</summary>
    Settled = 0,

    /// <summary>A definitive refusal. Provably nothing moved (locally asserted).</summary>
    Rejected = 1,

    /// <summary>An unconfirmable outcome: reported as settled without a usable reference.</summary>
    OutcomeUnknown = 2,

    /// <summary>No gateway is configured; nothing was sent anywhere.</summary>
    NotConfigured = 3
}

/// <summary>
/// Test-host-only <see cref="IPaymobClient"/>. It replaces the provider-capable
/// registration so that Families/CareHomes booking refund paths resolve a
/// double that never opens a socket. The refund outcome is scripted in-process
/// by the fixture provisioner (never read from configuration or the
/// environment), so ambiguity/failure states are produced deliberately and are
/// always labelled synthetic.
/// </summary>
public sealed class ScriptedPaymobClient : IPaymobClient
{
    /// <summary>Marker that every synthetic reference carries so no reader can mistake it for provider evidence.</summary>
    public const string SyntheticReferencePrefix = "sanad-portal-fixture-synthetic";

    private long _sequence;

    /// <summary>
    /// The outcome the next <see cref="RefundPaymentAsync"/> call returns. Set
    /// by the provisioner immediately before a refund command; defaults to the
    /// honest local settle.
    /// </summary>
    public ScriptedRefundOutcome NextRefundOutcome { get; set; } = ScriptedRefundOutcome.Settled;

    /// <summary>Count of refund calls observed; used only for fixture diagnostics, never currency evidence.</summary>
    public long RefundCallCount => Interlocked.Read(ref _sequence);

    public Task<Result<PaymobPaymentIntent>> CreatePaymentIntentAsync(
        PaymobPaymentIntentInput input,
        CancellationToken cancellationToken = default)
    {
        string order = NextReference("order");
        return Task.FromResult(Result<PaymobPaymentIntent>.Success(new PaymobPaymentIntent(
            order,
            $"synthetic-intention-{order}",
            $"synthetic-secret-{order}",
            "synthetic-public-key")));
    }

    public Task<Result<PaymobPaymentIntent>> CreateSubscriptionPaymentIntentAsync(
        PaymobSubscriptionPaymentIntentInput input,
        CancellationToken cancellationToken = default)
    {
        string order = NextReference("subscription-order");
        return Task.FromResult(Result<PaymobPaymentIntent>.Success(new PaymobPaymentIntent(
            order,
            $"synthetic-intention-{order}",
            $"synthetic-secret-{order}",
            "synthetic-public-key",
            RecurringRenewalSupported: true)));
    }

    public Task<Result> UpdateSubscriptionAmountAsync(
        string providerSubscriptionId,
        decimal targetRecurringGross,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task<Result<string?>> RefundPaymentAsync(
        string paymobTransactionId,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _sequence);

        return NextRefundOutcome switch
        {
            ScriptedRefundOutcome.Settled =>
                Task.FromResult(Result<string?>.Success(NextReference("refund"))),

            // A definitive provider refusal: the shared money contract reads
            // this code as Rejected (provably nothing moved).
            ScriptedRefundOutcome.Rejected =>
                Task.FromResult(Result<string?>.Failure(new Error(
                    "Paymob.RefundRejected",
                    "Synthetic local refusal (no provider was contacted)."))),

            ScriptedRefundOutcome.NotConfigured =>
                Task.FromResult(Result<string?>.Failure(new Error(
                    "Paymob.NotConfigured",
                    "Synthetic local not-configured outcome (no provider was contacted)."))),

            // The shared contract maps a 2xx without a usable reference to
            // OutcomeUnknown: movement cannot be proven, so reconciliation is
            // the only honest follow-up.
            _ => Task.FromResult(Result<string?>.Success(null))
        };
    }

    /// <summary>
    /// Every reference this double produces carries the synthetic marker, so a
    /// readback is refused rather than fabricated: no socket is ever opened and
    /// a synthetic reference can never be mistaken for provider evidence.
    /// </summary>
    public Task<Result<PaymobRefundVerification>> VerifyRefundTransactionAsync(
        string refundTransactionId,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result<PaymobRefundVerification>.Failure(new Error(
            "Paymob.VerificationUnavailable",
            "Synthetic fixture transactions cannot be verified against a provider.")));

    private string NextReference(string kind) =>
        $"{SyntheticReferencePrefix}-{kind}-{Interlocked.Increment(ref _sequence):x12}";
}

/// <summary>
/// Test-host-only <see cref="Ahlena.Modules.Finance.Application.ISubscriptionRefundGateway"/>.
/// Finance's duplicate-charge claim execution resolves this seam; the double
/// produces scripted outcomes with synthetic references and never calls a
/// provider. Preparation always reports a synthetic charge transaction so the
/// claim store can stage real execution state against the double.
/// </summary>
public sealed class ScriptedSubscriptionRefundGateway : Ahlena.Modules.Finance.Application.ISubscriptionRefundGateway
{
    private readonly ScriptedPaymobClient _script;

    public ScriptedSubscriptionRefundGateway(ScriptedPaymobClient script) => _script = script;

    public Task<Ahlena.Modules.Finance.Application.SubscriptionRefundGatewayPreparation> PrepareAsync(
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        if (paymentAttemptId == Guid.Empty)
        {
            return Task.FromResult(Ahlena.Modules.Finance.Application.SubscriptionRefundGatewayPreparation.Missing());
        }

        return Task.FromResult(
            Ahlena.Modules.Finance.Application.SubscriptionRefundGatewayPreparation.WithChargeTransaction(
                $"{ScriptedPaymobClient.SyntheticReferencePrefix}-charge-{paymentAttemptId:N}"));
    }

    public Task<Ahlena.Modules.Finance.Application.SubscriptionRefundGatewayResult> RefundAsync(
        string chargeTransactionId,
        string currency,
        long awardMinorUnits,
        CancellationToken cancellationToken)
    {
        Ahlena.Modules.Finance.Application.SubscriptionRefundGatewayResult result = _script.NextRefundOutcome switch
        {
            ScriptedRefundOutcome.Settled => new(
                Ahlena.Modules.Finance.Application.SubscriptionRefundGatewayOutcome.Refunded,
                $"{ScriptedPaymobClient.SyntheticReferencePrefix}-refund-{Guid.NewGuid():N}",
                null),

            ScriptedRefundOutcome.Rejected => new(
                Ahlena.Modules.Finance.Application.SubscriptionRefundGatewayOutcome.Rejected,
                null,
                "Paymob.RefundRejected"),

            ScriptedRefundOutcome.NotConfigured => new(
                Ahlena.Modules.Finance.Application.SubscriptionRefundGatewayOutcome.NotConfigured,
                null,
                "Paymob.NotConfigured"),

            _ => Ahlena.Modules.Finance.Application.SubscriptionRefundGatewayResult
                .UnknownBecauseReferenceMissing("Paymob.RefundOutcomeUnknown")
        };

        return Task.FromResult(result);
    }
}

/// <summary>
/// Test-host-only <see cref="IDateTimeProvider"/>. It exists so the fixture
/// provisioner can pin the instants it writes explicitly. It is an honest local
/// override with real limits: domain setters that call <c>DateTime.UtcNow</c>
/// directly (Families/Finance audit fields) are NOT controlled by this seam,
/// and the guide says so instead of claiming full clock control.
/// </summary>
public sealed class PortalFixtureClock(DateTime instantUtc) : IDateTimeProvider
{
    public DateTime UtcNow { get; } = EnsureUtc(instantUtc);

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

/// <summary>
/// Test-host-only <see cref="ISmsSender"/>: records the attempt and returns
/// immediately. It is registered so no SMS provider can be reached even when
/// live SMS configuration is present in appsettings, user secrets or the
/// environment.
/// </summary>
public sealed class PortalFixtureInertSmsSender : ISmsSender
{
    private long _count;

    public long SendCount => Interlocked.Read(ref _count);

    public Task SendVerificationCodeAsync(
        string phoneNumber,
        string code,
        VerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test-host-only <see cref="IEmailSender"/>: records the attempt and returns
/// immediately. No SMTP connection is possible through this seam.
/// </summary>
public sealed class PortalFixtureInertEmailSender : IEmailSender
{
    private long _count;

    public long SendCount => Interlocked.Read(ref _count);

    public Task SendEmailAsync(string email, string subject, string body, string? correlationId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        return Task.CompletedTask;
    }

    public Task SendVerificationCodeAsync(string email, string code, VerificationPurpose purpose, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        return Task.CompletedTask;
    }

    public Task SendFamilyInvitationAsync(string email, string familyName, string inviteLink, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        return Task.CompletedTask;
    }

    public Task SendSupportRequestAsync(string senderName, string? senderEmail, string senderPhoneNumber, string accountTypes, string subject, string message, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _count);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Test-host-only <see cref="IPushSender"/>: never configured and never sends.
/// It is registered so no FCM credential can be reached even if one is present
/// in configuration.
/// </summary>
public sealed class PortalFixtureInertPushSender : IPushSender
{
    public bool IsConfigured => false;

    public Task<PushSendResult> SendAsync(
        string token,
        string title,
        string body,
        IReadOnlyDictionary<string, string> data,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PushSendResult(PushSendOutcome.NotConfigured, null, "Push.NotConfigured"));
}
