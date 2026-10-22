using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Families.Application.Subscriptions;

/// <summary>
/// Which clause-9 subscription money outcome a request is about.
/// <para>
/// Clause 9 and Owner decision #6 require three <em>distinct</em> reasons with
/// three distinct outcomes; the Families side names them once here and maps
/// them one-to-one onto the Finance seam's
/// <see cref="Finance.Domain.SubscriptionRefundReason"/> so no caller
/// re-derives the money rule:
/// </para>
/// <list type="bullet">
/// <item><description>Cancellation retains paid access and refunds nothing for the current period.</description></item>
/// <item><description>A duplicate/erroneous charge is refunded in full, never prorated.</description></item>
/// <item><description>An Ahlena service shutdown refunds the unused portion of the current paid period.</description></item>
/// </list>
/// </summary>
public enum SubscriptionMoneyOutcome
{
    CancelRenewal = 1,
    DuplicateCharge = 2,
    ServiceShutdown = 3
}

public static class SubscriptionRefundErrors
{
    public static readonly Error NotOwner =
        new("Subscriptions.Refund.NotOwner", "Only the family owner can request a subscription refund.");

    public static readonly Error SubscriptionNotFound =
        new("Subscriptions.Refund.SubscriptionNotFound", "The current subscription was not found.");

    public static readonly Error NothingToRefund =
        new("Subscriptions.Refund.NothingToRefund", "There is no refundable subscription charge or unused paid period.");

    /// <summary>
    /// Malformed <em>input</em> (a non-UTC instant, an unusable id) mapped to a
    /// clean 400, kept distinct from <see cref="NothingToRefund"/> so "you sent a
    /// bad instant" never masquerades as "there is nothing refundable".</summary>
    public static readonly Error MalformedInput =
        new("Subscriptions.Refund.MalformedInput", "The subscription refund request is malformed.");

    public static readonly Error CaseRequired =
        new("Subscriptions.Refund.CaseRequired", "A refund case is required.");

    public static readonly Error FinanceRejected =
        new("Subscriptions.Refund.FinanceRejected", "The subscription refund was not accepted by shared finance.");

    public static readonly Error IdempotencyConflict =
        new("Subscriptions.Refund.IdempotencyConflict", "This idempotency key was already used with different content.");

    public static readonly Error NotCurrent =
        new("Subscriptions.Refund.NotCurrent", "Only the current subscription can be refunded.");

    /// <summary>
    /// The award would exceed the CAPTURE's remaining refundable funds (Owner
    /// decisions #4 and #6). Finance owns the cumulative claim, so this is the
    /// cross-case guard surfacing to the caller: another refund case against the
    /// same paid period has already claimed that money.
    /// </summary>
    public static readonly Error ExceedsCaptured =
        new("Subscriptions.Refund.ExceedsCaptured", "The award exceeds the capture's remaining refundable funds.");

    /// <summary>
    /// The succeeded settlement that paid for the current period could not be
    /// resolved, so the shutdown award has no capture to draw on. Fails closed:
    /// attaching the refund to a lifetime total or an arbitrary latest attempt
    /// would refund money that did not pay for the period.
    /// </summary>
    public static readonly Error CaptureNotResolved =
        new("Subscriptions.Refund.CaptureNotResolved", "The payment that captured the current period could not be resolved.");
}

/// <summary>
/// The recorded outcome of a subscription money movement, as the caller reads
/// it back: the refund case correlation id, the awarded amount and the distinct
/// reason that produced it.
/// </summary>
public sealed record SubscriptionRefundResponse(
    Guid RefundCaseId,
    long AwardedMinorUnits,
    string Currency,
    SubscriptionMoneyOutcome Outcome,
    bool IsReplay);

/// <summary>
/// Clause 9: an erroneous or duplicate subscription charge is refunded in full.
/// <para>
/// Orchestration lives with the purchasable owner (W3 design pin item 3): this
/// Families command decides <em>which</em> subscription charge is duplicated and
/// what the full amount is, then records the movement through the
/// <see cref="ISubscriptionRefundRecorder"/> Finance seam. Finance alone
/// persists money (#35 movement semantics); the refund correlation id is
/// derived from the payment attempt being corrected so the case is stable under
/// a retried callback, and the idempotency key is the caller's own M7 key (the
/// column-level unique constraint arbitrates replays).
/// </para>
/// </summary>
public sealed record RefundDuplicateSubscriptionChargeCommand(
    UserId ActorUserId,
    Guid PaymentAttemptId,
    string? IdempotencyKey = null,
    string? EvidenceReference = null,
    DateTime? RecordedOnUtc = null) : ICommand<SubscriptionRefundResponse>;

public sealed class RefundDuplicateSubscriptionChargeCommandHandler
    : ICommandHandler<RefundDuplicateSubscriptionChargeCommand, SubscriptionRefundResponse>
{
    private readonly IFamiliesDbContext _db;
    private readonly ISubscriptionRefundRecorder _refunds;

    public RefundDuplicateSubscriptionChargeCommandHandler(
        IFamiliesDbContext db,
        ISubscriptionRefundRecorder refunds)
    {
        _db = db;
        _refunds = refunds;
    }

    public async Task<Result<SubscriptionRefundResponse>> Handle(
        RefundDuplicateSubscriptionChargeCommand request,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.ActorUserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.ActorUserId))
            return Result<SubscriptionRefundResponse>.Failure(SubscriptionRefundErrors.NotOwner);

        SubscriptionPaymentAttempt? attempt = await _db.SubscriptionPaymentAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.PaymentAttemptId && item.FamilyId == family.Id,
                cancellationToken);
        if (attempt is null || attempt.Status != SubscriptionPaymentAttemptStatus.Succeeded)
            return Result<SubscriptionRefundResponse>.Failure(SubscriptionRefundErrors.NothingToRefund);

        // A duplicate charge is refunded IN FULL (clause 9) — the whole captured
        // payable amount, never prorated. The refund case is anchored on the
        // corrected payment attempt so a retry targets the same case.
        long fullAmountMinorUnits = ToMinorUnits(attempt.TotalPayable);
        Guid refundCaseId = DuplicateChargeCaseId(attempt.Id);

        // m3: a supplied instant must be UTC, failing with the same clean
        // malformed-input 400 the shutdown path uses — never a deep 409 from the
        // Finance seam's UTC guard.
        if (request.RecordedOnUtc is { Kind: not DateTimeKind.Utc })
            return Result<SubscriptionRefundResponse>.Failure(SubscriptionRefundErrors.MalformedInput);
        DateTime recordedOnUtc = request.RecordedOnUtc ?? DateTime.UtcNow;

        // M4: the refund ceiling is the CAPTURE, not the subscription. A duplicate
        // charge draws on the very attempt it is correcting and on nothing else,
        // so money already consumed in an older period can never supply headroom
        // for this one. The captured amount is the same server-derived figure the
        // award is computed from — never an all-time sum, never caller-supplied.
        if (attempt.SubscriptionId is not { } subscriptionId || subscriptionId == Guid.Empty)
            return Result<SubscriptionRefundResponse>.Failure(SubscriptionRefundErrors.MalformedInput);

        Result<SubscriptionRefundSnapshot> recorded = await _refunds.RecordAsync(
            refundCaseId,
            family.Id.Value,
            subscriptionId,
            attempt.Id,
            fullAmountMinorUnits,
            Sanad.Modules.Finance.Domain.Money.FromMinorUnits(fullAmountMinorUnits),
            SubscriptionRefundReason.DuplicateCharge,
            request.IdempotencyKey,
            recordedOnUtc,
            request.ActorUserId.Value,
            request.EvidenceReference,
            cancellationToken);

        if (recorded.IsFailure)
            return Result<SubscriptionRefundResponse>.Failure(MapError(recorded.Error));

        return Result<SubscriptionRefundResponse>.Success(new SubscriptionRefundResponse(
            recorded.Value.RefundCaseId,
            recorded.Value.AwardedMinorUnits,
            recorded.Value.Currency,
            SubscriptionMoneyOutcome.DuplicateCharge,
            recorded.Value.IsReplay));
    }

    /// <summary>
    /// A stable refund case per corrected charge: the same payment attempt always
    /// maps to the same case, so a replayed or raced request can never open a
    /// second case for one erroneous charge (#17 duplicate-refund prevention).
    /// </summary>
    private static Guid DuplicateChargeCaseId(Guid paymentAttemptId)
    {
        Span<byte> source = stackalloc byte[16];
        paymentAttemptId.TryWriteBytes(source);
        // Domain-separate the correlation space so an id can never collide with
        // another module's derivation of the same payment-attempt bytes.
        source[0] ^= 0xD0;
        return new Guid(source);
    }

    private static long ToMinorUnits(decimal majorUnits) =>
        checked((long)decimal.Round(majorUnits * 100m, 0, MidpointRounding.ToEven));

    internal static Error MapError(Error error) => error.Code switch
    {
        "Finance.SubscriptionRefund.IdempotencyConflict" => SubscriptionRefundErrors.IdempotencyConflict,
        "Finance.SubscriptionRefund.CaseRequired" => SubscriptionRefundErrors.CaseRequired,
        "Finance.SubscriptionRefund.ExceedsCaptured" => SubscriptionRefundErrors.ExceedsCaptured,
        "Finance.SubscriptionRefund.CaptureRequired" => SubscriptionRefundErrors.CaptureNotResolved,
        _ => new Error(SubscriptionRefundErrors.FinanceRejected.Code, error.Message)
    };
}

/// <summary>
/// Clause 9 / Owner decision #6: an Ahlena service shutdown refunds the
/// <em>unused portion</em> of the current paid period, prorated at the locked
/// ToEven rounding (#4) and floored at zero (#31).
/// <para>
/// The proration is authored here because the Families side owns the paid-period
/// boundaries and the settled amount of the current period; the resulting
/// movement is recorded through the same Finance seam, so the movement
/// accounting, the timeout gate and the append-only ledger all still apply.
/// </para>
/// <para>
/// M5: the base is the consideration settled for the <em>current</em> paid
/// period — <c>FamilySubscription.CurrentPeriodGross</c>, the snapshot the
/// subscription itself writes when it establishes a period
/// (<see cref="FamilySubscription.SetCurrentPeriodChargeSnapshot"/> on the
/// initial capture and on every renewal, and
/// <see cref="FamilySubscription.ApplyPaidPlanChange"/> with the target
/// period's gross). It is therefore not an all-time sum of renewals, and a paid
/// plan change is not treated as additive: the domain already folded the
/// prorated credit into that snapshot. The plan's list <c>Price</c> is never used
/// as a fallback, so discounts on the settled charge are honoured.
/// </para>
/// <para>
/// M4 + M5: the award is drawn against the <em>capture</em> that paid for this
/// period — the succeeded settlement that established it, with that period's
/// settled consideration as the ceiling. Finance owns the cumulative claim and
/// enforces, atomically, that a duplicate-charge award and a shutdown award for
/// the same capture can never exceed what that capture paid, in either order and
/// under any key. An older, already-consumed period therefore supplies no
/// headroom. If that capture cannot be resolved, the path fails closed with
/// <c>Subscriptions.Refund.CaptureNotResolved</c> rather than attaching the
/// refund to a lifetime total or an arbitrary attempt.
/// </para>
/// </summary>
public sealed record RefundSubscriptionServiceShutdownCommand(
    Guid SubscriptionId,
    UserId ActorUserId,
    DateTime ShutdownOnUtc,
    string? IdempotencyKey = null,
    string? EvidenceReference = null) : ICommand<SubscriptionRefundResponse>;

public sealed class RefundSubscriptionServiceShutdownCommandHandler
    : ICommandHandler<RefundSubscriptionServiceShutdownCommand, SubscriptionRefundResponse>
{
    private readonly IFamiliesDbContext _db;
    private readonly ISubscriptionRefundRecorder _refunds;

    public RefundSubscriptionServiceShutdownCommandHandler(
        IFamiliesDbContext db,
        ISubscriptionRefundRecorder refunds)
    {
        _db = db;
        _refunds = refunds;
    }

    public async Task<Result<SubscriptionRefundResponse>> Handle(
        RefundSubscriptionServiceShutdownCommand request,
        CancellationToken cancellationToken)
    {
        if (request.SubscriptionId == Guid.Empty)
            return Result<SubscriptionRefundResponse>.Failure(SubscriptionRefundErrors.SubscriptionNotFound);

        // m4: a non-UTC instant is malformed input (clean 400), not "nothing to
        // refund" — the latter must mean the money rule produced zero, never that
        // the request itself was unusable.
        if (request.ShutdownOnUtc.Kind != DateTimeKind.Utc)
            return Result<SubscriptionRefundResponse>.Failure(SubscriptionRefundErrors.MalformedInput);

        FamilySubscription? subscription = await _db.FamilySubscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == request.SubscriptionId, cancellationToken);
        if (subscription is null)
            return Result<SubscriptionRefundResponse>.Failure(SubscriptionRefundErrors.SubscriptionNotFound);
        if (!subscription.IsCurrent)
            return Result<SubscriptionRefundResponse>.Failure(SubscriptionRefundErrors.NotCurrent);

        // M4 + M5: resolve the CAPTURE that paid for the current period. The
        // ceiling is that capture's settled consideration — never a lifetime
        // total, so an older period's already-consumed money cannot supply
        // headroom for this one. If the period's capture cannot be resolved we
        // fail closed: attaching the refund to an arbitrary "latest" attempt would
        // claim money that did not pay for the period.
        (Guid PaymentAttemptId, long CapturedMinorUnits)? resolved =
            await ResolveCurrentPeriodCaptureAsync(subscription, cancellationToken);
        if (resolved is not { } currentPeriod)
            return Result<SubscriptionRefundResponse>.Failure(SubscriptionRefundErrors.CaptureNotResolved);

        if (currentPeriod.CapturedMinorUnits <= 0)
            return Result<SubscriptionRefundResponse>.Failure(SubscriptionRefundErrors.NothingToRefund);

        long awardMinorUnits = ProrateUnusedPeriod(
            subscription,
            request.ShutdownOnUtc,
            currentPeriod.CapturedMinorUnits);

        Guid refundCaseId = ShutdownCaseId(subscription.Id);

        Result<SubscriptionRefundSnapshot> recorded = await _refunds.RecordAsync(
            refundCaseId,
            subscription.FamilyId.Value,
            subscription.Id,
            currentPeriod.PaymentAttemptId,
            currentPeriod.CapturedMinorUnits,
            Sanad.Modules.Finance.Domain.Money.FromMinorUnits(awardMinorUnits),
            SubscriptionRefundReason.ServiceShutdown,
            request.IdempotencyKey,
            request.ShutdownOnUtc,
            request.ActorUserId.Value,
            request.EvidenceReference,
            cancellationToken);

        if (recorded.IsFailure)
            return Result<SubscriptionRefundResponse>.Failure(
                RefundDuplicateSubscriptionChargeCommandHandler.MapError(recorded.Error));

        return Result<SubscriptionRefundResponse>.Success(new SubscriptionRefundResponse(
            recorded.Value.RefundCaseId,
            recorded.Value.AwardedMinorUnits,
            recorded.Value.Currency,
            SubscriptionMoneyOutcome.ServiceShutdown,
            recorded.Value.IsReplay));
    }

    /// <summary>
    /// Resolves the CAPTURE that paid for the current period: the succeeded
    /// attempt whose settlement established the period, plus the consideration
    /// settled for that period (M4 and M5 together).
    /// <para>
    /// The window is the subscription's own source-backed period — the stored
    /// period end rewound by one cycle — so the attempt selected is the one that
    /// settled inside this period, not merely the newest row in the database.
    /// Within the window the newest settlement wins, because that is the write
    /// that last set <see cref="FamilySubscription.CurrentPeriodGross"/> (a paid
    /// plan change overwrites it with the target period's gross).
    /// </para>
    /// <para>
    /// The amount is <see cref="FamilySubscription.CurrentPeriodGross"/> when the
    /// snapshot exists, otherwise the attempt's own plan-change-aware
    /// consideration (<c>BasePrice</c> for a paid plan change, <c>TotalPayable</c>
    /// otherwise). The plan's list <c>Price</c> is never used, so discounts on the
    /// settled charge survive, and a plan change is never added on top of the
    /// period it was credited against.
    /// </para>
    /// <para>
    /// Returns <see langword="null"/> when no succeeded settlement falls inside the
    /// period. The caller fails closed with a distinct error: falling back to a
    /// lifetime total or to an arbitrary latest attempt would attach the refund to
    /// money that did not pay for this period.
    /// </para>
    /// </summary>
    private async Task<(Guid PaymentAttemptId, long CapturedMinorUnits)?> ResolveCurrentPeriodCaptureAsync(
        FamilySubscription subscription,
        CancellationToken cancellationToken)
    {
        DateTime periodStart = PeriodStart(subscription);
        DateTime periodEnd = subscription.CurrentPeriodEndsOnUtc;

        SubscriptionPaymentAttempt? attempt = await _db.SubscriptionPaymentAttempts
            .AsNoTracking()
            .Where(item => item.SubscriptionId == subscription.Id
                && item.Status == SubscriptionPaymentAttemptStatus.Succeeded
                && (item.SettledOnUtc ?? item.CreatedOnUtc) >= periodStart
                && (item.SettledOnUtc ?? item.CreatedOnUtc) < periodEnd)
            .OrderByDescending(item => item.SettledOnUtc ?? item.CreatedOnUtc)
            .ThenByDescending(item => item.CreatedOnUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (attempt is null)
            return null;

        long consideration = subscription.CurrentPeriodGross is { } settledGross
            ? ToMinorUnits(settledGross)
            : ToMinorUnits(attempt.IsPlanChange ? attempt.BasePrice : attempt.TotalPayable);

        return (attempt.Id, consideration);
    }

    /// <summary>
    /// The unused portion of the current paid period, on the consideration settled
    /// for that period (M5), floored at zero (#31). The cross-case ceiling of
    /// decision #4 is enforced by Finance against the same capture, so no local cap
    /// is applied here: a local cap could only mask a data problem and would
    /// silently trim an award the seam is supposed to refuse outright.
    /// <para>
    /// A shutdown at or after the period end, or one second before it, may
    /// compute exactly zero (#31): the zero award is recorded as computed and
    /// never rounded up.
    /// </para>
    /// </summary>
    private static long ProrateUnusedPeriod(
        FamilySubscription subscription,
        DateTime shutdownOnUtc,
        long currentPeriodConsiderationMinorUnits)
    {
        DateTime periodStart = PeriodStart(subscription);
        DateTime periodEnd = subscription.CurrentPeriodEndsOnUtc;

        if (shutdownOnUtc >= periodEnd)
            return 0;

        decimal totalTicks = (periodEnd - periodStart).Ticks;
        if (totalTicks <= 0)
            return 0;

        decimal unusedTicks = (periodEnd - shutdownOnUtc).Ticks < 0 ? 0m : (periodEnd - shutdownOnUtc).Ticks;
        decimal unusedRatio = unusedTicks / totalTicks;

        return checked((long)decimal.Round(
            currentPeriodConsiderationMinorUnits * unusedRatio,
            0,
            MidpointRounding.ToEven));
    }

    /// <summary>
    /// The paid-period start is not stored on the snapshot, but the period end
    /// and the cycle are, so the start is the period end rewound by exactly one
    /// cycle. This matches how the period was advanced on creation/renewal.
    /// </summary>
    private static DateTime PeriodStart(FamilySubscription subscription) =>
        subscription.Cycle switch
        {
            SubscriptionCycle.Monthly => subscription.CurrentPeriodEndsOnUtc.AddMonths(-1),
            SubscriptionCycle.Annual => subscription.CurrentPeriodEndsOnUtc.AddYears(-1),
            _ => subscription.CurrentPeriodEndsOnUtc.AddMonths(-1)
        };

    private static Guid ShutdownCaseId(Guid subscriptionId)
    {
        Span<byte> source = stackalloc byte[16];
        subscriptionId.TryWriteBytes(source);
        source[0] ^= 0x5D;
        return new Guid(source);
    }

    private static long ToMinorUnits(decimal majorUnits) =>
        checked((long)decimal.Round(majorUnits * 100m, 0, MidpointRounding.ToEven));
}
