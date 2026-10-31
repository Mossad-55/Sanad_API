using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Families.Domain.Subscriptions;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.Modules.Families.Application.Subscriptions;

/// <summary>
/// Stable family-facing error codes for the duplicate-charge support-claim
/// workflow. They deliberately reuse the shipped refund surface's codes
/// (NotOwner / NothingToRefund / MalformedInput / IdempotencyConflict /
/// ExceedsCaptured / FinanceRejected) so existing clients read the same
/// contract; the flags that are NEW to the claim workflow get their own codes
/// (MigratedToClaim, CaptureAlreadyClaimed, ClaimNotFound).
/// </summary>
public static class DuplicateChargeClaimSubmissionErrors
{
    /// <summary>
    /// The legacy direct-award duplicate-charge refund is retired. The same
    /// address now requires the explicit opt-in, fail-closed, so an unaware
    /// client can never receive an auto-award again.
    /// </summary>
    public static readonly Error MigratedToClaim =
        new("Subscriptions.Refund.MigratedToClaim",
            "Duplicate-charge refunds are now support-approved claims. Resubmit with claimSubmission: true.");

    public static readonly Error NotOwner =
        new("Subscriptions.Refund.NotOwner", "Only the family owner may submit a duplicate-charge claim.");

    /// <summary>Probe-safe 404: a foreign or non-succeeded attempt simply does not exist.</summary>
    public static readonly Error NothingToRefund =
        new("Subscriptions.Refund.NothingToRefund", "No succeeded duplicate charge to claim was found.");

    public static readonly Error MalformedInput =
        new("Subscriptions.Refund.MalformedInput", "The duplicate-charge claim request is not valid.");

    public static readonly Error IdempotencyConflict =
        new("Subscriptions.Refund.IdempotencyConflict", "This idempotency key was already used with different content.");

    public static readonly Error CaptureAlreadyClaimed =
        new("Subscriptions.Refund.CaptureAlreadyClaimed", "This capture is already claimed or refunded.");

    public static readonly Error ExceedsCaptured =
        new("Subscriptions.Refund.ExceedsCaptured", "The capture no longer holds enough refundable funds; contact support.");

    public static readonly Error FinanceRejected =
        new("Subscriptions.Refund.FinanceRejected", "The refund could not be recorded; contact support.");

    /// <summary>Probe-safe 404 for family claim reads: a foreign claim is simply not found.</summary>
    public static readonly Error ClaimNotFound =
        new("Subscriptions.Refund.ClaimNotFound", "No duplicate-charge claim was found.");
}

/// <summary>
/// Submit a family duplicate-charge claim for SUPPORT approval (clause 9,
/// Owner-accepted design): submission derives everything server-side â€” the
/// succeeded capture, the FULL verified basis from the attempt's settled
/// payable â€” but never moves money and never awards a ledger entry. Approval
/// is the Finance support decision; execution is a separate, SuperAdmin-only
/// step. The old auto-award is retired behind an explicit opt-in at the
/// transport, and the legacy handler is fail-closed.
/// </summary>
public sealed record SubmitDuplicateChargeClaimCommand(
    UserId ActorUserId,
    Guid PaymentAttemptId,
    string? IdempotencyKey = null,
    string? EvidenceReference = null,
    DateTime? CustomerReportedOnUtc = null) : ICommand<FamilyDuplicateChargeClaimView>;

public sealed class SubmitDuplicateChargeClaimCommandHandler(
    IFamiliesDbContext db,
    IDuplicateChargeClaimStore claims,
    IDateTimeProvider clock)
    : ICommandHandler<SubmitDuplicateChargeClaimCommand, FamilyDuplicateChargeClaimView>
{
    public async Task<Result<FamilyDuplicateChargeClaimView>> Handle(
        SubmitDuplicateChargeClaimCommand request,
        CancellationToken cancellationToken)
    {
        // Same malformed-input contract as the legacy path: bounds and UTC
        // validation fail with the inherited 400 code, never a deep seam error.
        if (request.PaymentAttemptId == Guid.Empty)
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimSubmissionErrors.MalformedInput);
        if ((request.IdempotencyKey?.Length ?? 0) > DuplicateChargeClaim.MaximumIdempotencyKeyLength ||
            (request.EvidenceReference?.Length ?? 0) > DuplicateChargeClaim.MaximumEvidenceReferenceLength)
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimSubmissionErrors.MalformedInput);
        if (request.CustomerReportedOnUtc is { Kind: not DateTimeKind.Utc })
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimSubmissionErrors.MalformedInput);
        DateTime reportedOnUtc = request.CustomerReportedOnUtc ?? clock.UtcNow;

        var family = await FamilyAccess.ResolveFamilyAsync(db, request.ActorUserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.ActorUserId))
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimSubmissionErrors.NotOwner);

        SubscriptionPaymentAttempt? attempt = await db.SubscriptionPaymentAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.PaymentAttemptId && item.FamilyId == family.Id,
                cancellationToken);
        if (attempt is null || attempt.Status != SubscriptionPaymentAttemptStatus.Succeeded)
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimSubmissionErrors.NothingToRefund);

        // A succeeded capture is not automatically a duplicate; the claim is
        // what Support verifies. The basis is the FULL server-derived payable
        // of that capture â€” never caller-supplied, never prorated (clause 9).
        if (attempt.SubscriptionId is not { } subscriptionId || subscriptionId == Guid.Empty)
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimSubmissionErrors.MalformedInput);

        long basisMinorUnits = DuplicateChargeClaimSubmissionMoney.ToMinorUnits(attempt.TotalPayable);
        if (basisMinorUnits <= 0)
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimSubmissionErrors.MalformedInput);

        Result<FamilyDuplicateChargeClaimView> submitted = await claims.SubmitAsync(
            new DuplicateChargeClaimSubmissionInput(
                family.Id.Value,
                subscriptionId,
                attempt.Id,
                SubscriptionRefundCaseIds.ForDuplicateCharge(attempt.Id),
                basisMinorUnits,
                request.IdempotencyKey,
                request.EvidenceReference,
                reportedOnUtc,
                clock.UtcNow),
            cancellationToken);

        if (submitted.IsSuccess)
            return submitted;

        // Map the Finance claim errors onto the family contract's stable codes.
        return Result<FamilyDuplicateChargeClaimView>.Failure(submitted.Error.Code switch
        {
            "Finance.DuplicateClaim.IdempotencyConflict" => DuplicateChargeClaimSubmissionErrors.IdempotencyConflict,
            "Finance.DuplicateClaim.CaptureAlreadyClaimed" or "Finance.DuplicateClaim.AlreadyAwarded" =>
                DuplicateChargeClaimSubmissionErrors.CaptureAlreadyClaimed,
            "Finance.DuplicateClaim.CaptureBasisUnavailable" => DuplicateChargeClaimSubmissionErrors.ExceedsCaptured,
            "Finance.DuplicateClaim.Invalid" or "Finance.DuplicateClaim.Conflict" or "Finance.DuplicateClaim.Unknown" =>
                DuplicateChargeClaimSubmissionErrors.FinanceRejected,
            _ => DuplicateChargeClaimSubmissionErrors.FinanceRejected
        });
    }
}

/// <summary>A family read of one of its own claims; a foreign claim id is a probe-safe 404.</summary>
public sealed record GetMyDuplicateChargeClaimQuery(
    UserId ActorUserId,
    Guid ClaimId) : IQuery<FamilyDuplicateChargeClaimView>;

public sealed record ListMyDuplicateChargeClaimsQuery(
    UserId ActorUserId) : IQuery<IReadOnlyList<FamilyDuplicateChargeClaimView>>;

public sealed class GetMyDuplicateChargeClaimQueryHandler(
    IFamiliesDbContext db,
    IDuplicateChargeClaimReader claims)
    : IQueryHandler<GetMyDuplicateChargeClaimQuery, FamilyDuplicateChargeClaimView>
{
    public async Task<Result<FamilyDuplicateChargeClaimView>> Handle(
        GetMyDuplicateChargeClaimQuery request,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(db, request.ActorUserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.ActorUserId))
            return Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimSubmissionErrors.NotOwner);

        FamilyDuplicateChargeClaimView? claim = await claims.GetForFamilyAsync(
            family.Id.Value,
            request.ClaimId,
            cancellationToken);

        // Probe-safe: another family's claim and an unknown id are the same 404.
        return claim is null
            ? Result<FamilyDuplicateChargeClaimView>.Failure(DuplicateChargeClaimSubmissionErrors.ClaimNotFound)
            : Result<FamilyDuplicateChargeClaimView>.Success(claim);
    }
}

public sealed class ListMyDuplicateChargeClaimsQueryHandler(
    IFamiliesDbContext db,
    IDuplicateChargeClaimReader claims)
    : IQueryHandler<ListMyDuplicateChargeClaimsQuery, IReadOnlyList<FamilyDuplicateChargeClaimView>>
{
    public async Task<Result<IReadOnlyList<FamilyDuplicateChargeClaimView>>> Handle(
        ListMyDuplicateChargeClaimsQuery request,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(db, request.ActorUserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.ActorUserId))
            return Result<IReadOnlyList<FamilyDuplicateChargeClaimView>>.Failure(DuplicateChargeClaimSubmissionErrors.NotOwner);

        IReadOnlyList<FamilyDuplicateChargeClaimView> list = await claims.ListForFamilyAsync(
            family.Id.Value,
            cancellationToken);

        return Result<IReadOnlyList<FamilyDuplicateChargeClaimView>>.Success(list);
    }
}

/// <summary>
/// The exact legacy decimalâ†’piastre conversion (checked, banker's rounding)
/// â€” kept byte-identical to the shipped duplicate-charge derivation so the
/// verified basis a family reads is the money the legacy path would have
/// derived for the same charge.
/// </summary>
internal static class DuplicateChargeClaimSubmissionMoney
{
    public static long ToMinorUnits(decimal majorUnits) =>
        checked((long)decimal.Round(majorUnits * 100m, 0, MidpointRounding.ToEven));
}
