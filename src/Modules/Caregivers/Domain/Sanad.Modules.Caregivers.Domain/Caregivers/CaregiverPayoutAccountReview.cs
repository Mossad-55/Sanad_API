using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

public sealed class CaregiverPayoutAccountReview :
    AggregateRoot<CaregiverPayoutAccountReviewId>
{
    private CaregiverPayoutAccountReview()
    {
    }

    private CaregiverPayoutAccountReview(
        CaregiverPayoutAccountReviewId id,
        CaregiverPayoutAccountId payoutAccountId,
        int accountRevision,
        PayoutAccountReviewDecision decision,
        UserId actorUserId,
        DateTime occurredOnUtc,
        string? verificationSource,
        string? reference,
        string? reason)
        : base(id)
    {
        PayoutAccountId = payoutAccountId;
        AccountRevision = accountRevision;
        Decision = decision;
        ActorUserId = actorUserId;
        OccurredOnUtc = occurredOnUtc;
        VerificationSource = verificationSource;
        Reference = reference;
        Reason = reason;
    }

    public CaregiverPayoutAccountId PayoutAccountId { get; private set; } = default!;

    public int AccountRevision { get; private set; }

    public PayoutAccountReviewDecision Decision { get; private set; }

    public UserId ActorUserId { get; private set; } = default!;

    public DateTime OccurredOnUtc { get; private set; }

    public string? VerificationSource { get; private set; }

    public string? Reference { get; private set; }

    public string? Reason { get; private set; }

    public static CaregiverPayoutAccountReview Create(
        CaregiverPayoutAccountId payoutAccountId,
        int accountRevision,
        PayoutAccountReviewDecision decision,
        UserId actorUserId,
        DateTime occurredOnUtc,
        string? verificationSource = null,
        string? reference = null,
        string? reason = null)
    {
        if (payoutAccountId == CaregiverPayoutAccountId.Empty)
        {
            throw new DomainException(
                "Payout account ID is required.");
        }

        if (accountRevision < 1)
        {
            throw new DomainException(
                "Reviewed account revision must be positive.");
        }

        if (!Enum.IsDefined(decision))
        {
            throw new DomainException(
                "Review decision is invalid.");
        }

        if (actorUserId == UserId.Empty)
        {
            throw new DomainException(
                "Reviewing actor is required.");
        }

        if (occurredOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Review timestamps must be UTC.");
        }

        return new CaregiverPayoutAccountReview(
            CaregiverPayoutAccountReviewId.New(),
            payoutAccountId,
            accountRevision,
            decision,
            actorUserId,
            occurredOnUtc,
            verificationSource,
            reference,
            reason);
    }
}

public enum PayoutAccountReviewDecision
{
    Verified = 1,
    Rejected = 2,
    Revoked = 3,
    Revealed = 4
}
