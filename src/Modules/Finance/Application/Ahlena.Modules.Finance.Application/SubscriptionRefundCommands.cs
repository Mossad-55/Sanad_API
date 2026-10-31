using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.Modules.Finance.Application;

public static class SubscriptionRefundErrors
{
    public static readonly Error CaseRequired =
        new("Finance.SubscriptionRefund.CaseRequired", "A refund case is required.");

    public static readonly Error FamilyRequired =
        new("Finance.SubscriptionRefund.FamilyRequired", "A family account is required.");

    /// <summary>The refund ceiling is per capture, so a funded award that cannot name its capture is malformed.</summary>
    public static readonly Error SubscriptionRequired =
        new("Finance.SubscriptionRefund.SubscriptionRequired", "The subscription this refund is claimed against is required.");

    /// <summary>
    /// The refund ceiling is per capture: a funded award must name the succeeded
    /// payment attempt it draws on, otherwise the cross-case guard would have no
    /// scope to serialize on.
    /// </summary>
    public static readonly Error CaptureRequired =
        new("Finance.SubscriptionRefund.CaptureRequired", "The payment attempt this refund is captured against is required.");

    public static readonly Error AmountInvalid =
        new("Finance.SubscriptionRefund.AmountInvalid", "The subscription refund amount is invalid.");

    public static readonly Error Invalid =
        new("Finance.SubscriptionRefund.Invalid", "The subscription refund movement is not valid.");

    public static readonly Error IdempotencyConflict =
        new("Finance.SubscriptionRefund.IdempotencyConflict", "This idempotency key was already used with different content.");

    /// <summary>
    /// The award would push the CAPTURE's cumulative refunds past the funds
    /// actually paid for it (Owner decisions #4 and #6). This is the cross-case
    /// guard: a duplicate-charge case and a shutdown case against the same paid
    /// period share one capture budget, so the second one cannot refund money the
    /// first already claimed — and an older period's already-consumed money can
    /// never supply headroom for a newer one. The seam refuses rather than
    /// silently trimming the award, because a trimmed duplicate-charge would no
    /// longer be "in full".
    /// </summary>
    public static readonly Error ExceedsCaptured =
        new("Finance.SubscriptionRefund.ExceedsCaptured", "The award exceeds the capture's remaining refundable funds.");

    public static readonly Error Conflict =
        new("Finance.SubscriptionRefund.Conflict", "The subscription refund case was changed concurrently; retry.");

    public static readonly Error Unknown =
        new("Finance.SubscriptionRefund.Unknown", "The subscription refund operation could not be completed.");
}

/// <summary>
/// The recorded movement outcome of one subscription refund request. The
/// <see cref="Reason"/> is part of the answer, not decoration: it is what makes
/// a duplicate-charge refund distinguishable from a shutdown proration and from
/// an ordinary cancel-renewal no-refund.
/// </summary>
public sealed record SubscriptionRefundSnapshot(
    Guid RefundCaseId,
    long AwardedMinorUnits,
    string Currency,
    SubscriptionRefundReason Reason,
    bool IsReplay,
    RefundLedgerEntryType EntryType,
    DateTime RecordedOnUtc);

/// <summary>
/// M7 idempotent record of a subscription money movement.
/// <para>
/// The caller — the purchasable owner, i.e. the Families subscription
/// cancellation / duplicate-charge / shutdown paths (W3 design pin item 3) —
/// supplies an <see cref="IdempotencyKey"/>. Finance enforces uniqueness with a
/// column-level <c>IdempotencyKey</c> plus a unique index
/// (<see cref="SubscriptionRefundRequest"/>), never a receipts table (design
/// pin item 4). A replay with the same key <em>and</em> the same content
/// returns the original outcome; the same key with any differing content is the
/// stable <c>Finance.SubscriptionRefund.IdempotencyConflict</c>.
/// </para>
/// <para>
/// The movement is appended through the frozen <see cref="RefundLedger"/>
/// aggregate, so the unresolved-timeout gate, the zero-award rule, the movement
/// accounting and the append-only structure all still apply — this seam adds
/// idempotency, it never bypasses the money core.
/// </para>
/// </summary>
public interface ISubscriptionRefundRecorder
{
    /// <param name="subscriptionId">
    /// Audit correlation: the subscription the capture belongs to. Indexed, never
    /// the refund ceiling.
    /// </param>
    /// <param name="paymentAttemptId">
    /// The <em>capture</em> the award is claimed against — the succeeded payment
    /// attempt whose settled consideration it may refund (for a duplicate charge,
    /// the duplicated attempt itself; for a shutdown, the attempt that paid for
    /// the current period). Required for any award above zero; it is the key of
    /// the shared budget, so two clause-9 cases against one paid period can never
    /// refund that period twice in either order.
    /// </param>
    /// <param name="capturedMinorUnits">
    /// What was actually paid for that specific capture/period, in piastres, as
    /// derived server-side by the purchase owner. It defines the ceiling of the
    /// budget; the cumulative claimed total is owned by Finance and never
    /// supplied here.
    /// </param>
    Task<Result<SubscriptionRefundSnapshot>> RecordAsync(
        Guid refundCaseId,
        Guid familyId,
        Guid subscriptionId,
        Guid paymentAttemptId,
        long capturedMinorUnits,
        Money awarded,
        SubscriptionRefundReason reason,
        string? idempotencyKey,
        DateTime recordedOnUtc,
        Guid? actorUserId,
        string? evidenceReference,
        CancellationToken cancellationToken);
}

public sealed record RecordSubscriptionRefundCommand(
    Guid RefundCaseId,
    Guid FamilyId,
    Guid SubscriptionId,
    Guid PaymentAttemptId,
    long CapturedMinorUnits,
    long AwardedMinorUnits,
    SubscriptionRefundReason Reason,
    DateTime RecordedOnUtc,
    Guid? ActorUserId = null,
    string? IdempotencyKey = null,
    string? EvidenceReference = null) : ICommand<SubscriptionRefundSnapshot>;

public sealed class RecordSubscriptionRefundCommandHandler(ISubscriptionRefundRecorder recorder)
    : ICommandHandler<RecordSubscriptionRefundCommand, SubscriptionRefundSnapshot>
{
    public async Task<Result<SubscriptionRefundSnapshot>> Handle(
        RecordSubscriptionRefundCommand request,
        CancellationToken cancellationToken)
    {
        if (request.RefundCaseId == Guid.Empty)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.CaseRequired);
        if (request.FamilyId == Guid.Empty)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.FamilyRequired);
        if (request.SubscriptionId == Guid.Empty)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.SubscriptionRequired);
        // Capture is required only for a funded award: a zero-piastre movement
        // claims no budget (cancel-renewal), so it needs none.
        if (request.AwardedMinorUnits > 0 && request.PaymentAttemptId == Guid.Empty)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.CaptureRequired);
        if (request.CapturedMinorUnits < 0)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.AmountInvalid);
        if (request.AwardedMinorUnits < 0)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.AmountInvalid);
        if (request.RecordedOnUtc.Kind != DateTimeKind.Utc)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.AmountInvalid);
        if (!Enum.IsDefined(request.Reason))
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.Invalid);

        return await recorder.RecordAsync(
            request.RefundCaseId,
            request.FamilyId,
            request.SubscriptionId,
            request.PaymentAttemptId,
            request.CapturedMinorUnits,
            Money.FromMinorUnits(request.AwardedMinorUnits),
            request.Reason,
            request.IdempotencyKey,
            request.RecordedOnUtc,
            request.ActorUserId,
            request.EvidenceReference,
            cancellationToken);
    }
}

/// <summary>
/// Finance-side persistence over <see cref="IFinanceDbContext"/>. The ledger
/// records the MOVEMENT (Owner decision #35): the awarded amount, never a
/// resulting balance.
/// </summary>
public sealed class SubscriptionRefundRecorder(IFinanceDbContext dbContext) : ISubscriptionRefundRecorder
{
    public async Task<Result<SubscriptionRefundSnapshot>> RecordAsync(
        Guid refundCaseId,
        Guid familyId,
        Guid subscriptionId,
        Guid paymentAttemptId,
        long capturedMinorUnits,
        Money awarded,
        SubscriptionRefundReason reason,
        string? idempotencyKey,
        DateTime recordedOnUtc,
        Guid? actorUserId,
        string? evidenceReference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(awarded);

        if (!Enum.IsDefined(reason))
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.Invalid);

        if (subscriptionId == Guid.Empty)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.SubscriptionRequired);
        // The ceiling is per capture. A funded award must name its capture or the
        // cross-case guard has nothing to serialize on; a zero-piastre award moves
        // no money and therefore claims no budget, so it needs none.
        if (awarded.IsPositive && paymentAttemptId == Guid.Empty)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.CaptureRequired);
        if (capturedMinorUnits < 0)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.AmountInvalid);
        // A negative award is malformed input, refused before it can touch the
        // shared budget or the append-only ledger.
        if (awarded.IsNegative)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.AmountInvalid);

        // The frozen money core records the MOVEMENT actor on an approved entry
        // (RefundLedger.Approve requires a non-empty Guid actor). A system
        // transition is represented by an audit actor, never by an absent one on
        // this path: a null/empty actor is malformed input, not a system move.
        if (actorUserId is not { } actor || actor == Guid.Empty)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.Invalid);

        string? key = NormalizeKey(idempotencyKey);
        if (idempotencyKey is not null && key is null)
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.Invalid);
        if (key is { Length: > SubscriptionRefundRequest.MaximumIdempotencyKeyLength })
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.Invalid);

        string reasonToken = ReasonToken(reason);
        string reference = Normalize(evidenceReference) is { } supplied
            ? $"{reasonToken};{supplied}"
            : reasonToken;

        try
        {
            // M7 try-lookup first, before any state pre-check, so a retry
            // replays the original outcome instead of appending a second
            // movement — and, critically, instead of claiming the shared budget
            // a second time.
            if (key is not null)
            {
                SubscriptionRefundRequest? stored = await dbContext.SubscriptionRefundRequests
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.FamilyId == familyId && x.IdempotencyKey == key, cancellationToken);
                if (stored is not null)
                {
                    RefundLedgerEntry? marker = await FirstEntryAsync(stored.RefundCaseId, cancellationToken);
                    if (marker is null)
                        return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.Unknown);

                    return stored.MatchesContent(refundCaseId, awarded.MinorUnits, reason, evidenceReference)
                        ? Result<SubscriptionRefundSnapshot>.Success(
                            ToSnapshot(stored.RefundCaseId, marker, reason, isReplay: true))
                        : Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.IdempotencyConflict);
                }
            }

            RefundLedger? existing = await dbContext.RefundLedgers
                .Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.RefundCaseId == refundCaseId, cancellationToken);

            // A case that already recorded a movement cannot record a second
            // one under a fresh key: the caller must replay its own key.
            if (existing is not null && FirstEntry(existing) is not null)
                return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.IdempotencyConflict);

            // The shared, cross-case budget of THIS CAPTURE (Owner decisions #4
            // and #6). The row is tracked deliberately: it is the serialization
            // point, and its optimistic-concurrency token must be part of the same
            // SaveChanges as the movement it protects.
            if (awarded.IsPositive)
            {
                SubscriptionRefundBudget? budget = await dbContext.SubscriptionRefundBudgets
                    .SingleOrDefaultAsync(x => x.PaymentAttemptId == paymentAttemptId, cancellationToken);

                if (budget is null)
                {
                    SubscriptionRefundBudget candidate = SubscriptionRefundBudget.Open(
                        paymentAttemptId, subscriptionId, familyId, capturedMinorUnits, recordedOnUtc);
                    if (!candidate.TryClaim(awarded.MinorUnits, capturedMinorUnits, recordedOnUtc))
                        return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.ExceedsCaptured);
                    dbContext.SubscriptionRefundBudgets.Add(candidate);
                }
                else if (!budget.TryClaim(awarded.MinorUnits, capturedMinorUnits, recordedOnUtc))
                {
                    // A refused claim mutates nothing, so no partial increase can
                    // leak into a later save.
                    return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.ExceedsCaptured);
                }
            }

            RefundLedger ledger = existing ?? RefundLedger.Open(refundCaseId);
            if (existing is null)
                dbContext.RefundLedgers.Add(ledger);

            RefundLedgerEntry entry = ledger.Approve(awarded, recordedOnUtc, actor, reference);

            if (key is not null)
            {
                dbContext.SubscriptionRefundRequests.Add(
                    SubscriptionRefundRequest.Create(
                        refundCaseId,
                        familyId,
                        subscriptionId,
                        key,
                        awarded.MinorUnits,
                        reason,
                        evidenceReference,
                        recordedOnUtc));
            }

            // One unit of work: the budget claim, the movement and the marker
            // commit together, or none of them do.
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<SubscriptionRefundSnapshot>.Success(
                ToSnapshot(refundCaseId, entry, reason, isReplay: false));
        }
        catch (DomainException ex)
        {
            return Result<SubscriptionRefundSnapshot>.Failure(new Error(SubscriptionRefundErrors.Invalid.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent award claimed the same budget first (or a concurrent
            // first-time claim inserted the row). Nothing of ours committed.
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.Conflict);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A raced replay lost the unique (family_id, idempotency_key) index,
            // or a raced first-time budget claim lost the unique
            // subscription_id index; the caller retries and the marker lookup
            // replays the winner's outcome.
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<SubscriptionRefundSnapshot>.Failure(SubscriptionRefundErrors.Unknown);
        }
    }

    private async Task<RefundLedgerEntry?> FirstEntryAsync(Guid refundCaseId, CancellationToken cancellationToken) =>
        await dbContext.RefundLedgerEntries
            .AsNoTracking()
            .Where(x => x.RefundCaseId == refundCaseId)
            .OrderBy(x => x.Sequence)
            .FirstOrDefaultAsync(cancellationToken);

    private static RefundLedgerEntry? FirstEntry(RefundLedger ledger) =>
        ledger.Entries.Count == 0 ? null : ledger.Entries.OrderBy(entry => entry.Sequence).First();

    private static SubscriptionRefundSnapshot ToSnapshot(
        Guid refundCaseId,
        RefundLedgerEntry entry,
        SubscriptionRefundReason reason,
        bool isReplay) =>
        new(
            refundCaseId,
            entry.Amount.MinorUnits,
            Money.CurrencyCode,
            reason,
            isReplay,
            entry.EntryType,
            entry.OccurredOnUtc);

    private static string ReasonToken(SubscriptionRefundReason reason) => reason switch
    {
        SubscriptionRefundReason.CancelRenewal => "reason:subscription-cancel-renewal",
        SubscriptionRefundReason.DuplicateCharge => "reason:subscription-duplicate-charge",
        SubscriptionRefundReason.ServiceShutdown => "reason:subscription-service-shutdown",
        _ => throw new DomainException("A subscription refund requires a known reason.")
    };

    private static string? Normalize(string? evidenceReference) =>
        string.IsNullOrWhiteSpace(evidenceReference) ? null : evidenceReference.Trim();

    private static string? NormalizeKey(string? idempotencyKey)
    {
        if (idempotencyKey is null)
            return null;

        string trimmed = idempotencyKey.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string message = current.Message ?? string.Empty;
            if (message.Contains("23505", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNIQUE CONSTRAINT FAILED", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Violation of UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
