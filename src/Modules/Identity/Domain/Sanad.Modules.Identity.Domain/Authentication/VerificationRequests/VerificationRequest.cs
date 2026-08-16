using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests.Events;

namespace Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

public sealed class VerificationRequest : AggregateRoot<VerificationRequestId>
{
    private VerificationRequest(
        VerificationRequestId id,
        UserId? userId,
        string target,
        string otpHash,
        VerificationChannel channel,
        VerificationPurpose purpose,
        DateTime expiresOnUtc)
        : base(id)
    {
        UserId = userId;
        Target = target;
        OtpHash = otpHash;
        Channel = channel;
        Purpose = purpose;

        Status = VerificationStatus.Pending;

        Attempts = 0;
        MaxAttempts = 5;

        CreatedOnUtc = DateTime.UtcNow;
        ExpiresOnUtc = expiresOnUtc;

        RaiseDomainEvent(
            new VerificationRequestCreatedDomainEvent(Id));
    }

    private VerificationRequest()
    {
    }

    public UserId? UserId { get; private set; }

    public string Target { get; private set; } = string.Empty;

    public string OtpHash { get; private set; } = string.Empty;

    public VerificationChannel Channel { get; private set; }

    public VerificationPurpose Purpose { get; private set; }

    public VerificationStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public int MaxAttempts { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime ExpiresOnUtc { get; private set; }

    public DateTime? VerifiedOnUtc { get; private set; }

    public DateTime? InvalidatedOnUtc { get; private set; }

    public static VerificationRequest Create(
        UserId? userId,
        string target,
        string otpHash,
        VerificationChannel channel,
        VerificationPurpose purpose,
        DateTime expiresOnUtc)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new DomainException(
                "Verification target cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(otpHash))
        {
            throw new DomainException(
                "OTP hash cannot be empty.");
        }

        return new VerificationRequest(
            VerificationRequestId.New(),
            userId,
            target.Trim(),
            otpHash,
            channel,
            purpose,
            expiresOnUtc);
    }

    public void Verify()
    {
        EnsurePending();

        if (IsExpired())
        {
            throw new DomainException(
                "Verification request has expired.");
        }

        Status = VerificationStatus.Verified;
        VerifiedOnUtc = DateTime.UtcNow;

        RaiseDomainEvent(
            new VerificationRequestVerifiedDomainEvent(Id));
    }

    public void RegisterFailedAttempt()
    {
        EnsurePending();

        if (IsExpired())
        {
            throw new DomainException(
                "Verification request has expired.");
        }

        Attempts++;

        if (Attempts >= MaxAttempts)
        {
            Invalidate();
        }
    }

    public void Invalidate()
    {
        EnsurePending();

        Status = VerificationStatus.Invalidated;
        InvalidatedOnUtc = DateTime.UtcNow;

        RaiseDomainEvent(
            new VerificationRequestInvalidatedDomainEvent(Id));
    }

    public void MarkExpired()
    {
        EnsurePending();

        Status = VerificationStatus.Expired;
    }

    public bool IsExpired()
    {
        return DateTime.UtcNow >= ExpiresOnUtc;
    }

    private void EnsurePending()
    {
        if (Status != VerificationStatus.Pending)
        {
            throw new DomainException(
                "Verification request is no longer pending.");
        }
    }
}