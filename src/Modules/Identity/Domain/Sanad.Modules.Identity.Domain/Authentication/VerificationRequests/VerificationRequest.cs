using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests.Events;
using Sanad.BuildingBlocks.Domain.ValueObjects;

namespace Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

public sealed class VerificationRequest : AggregateRoot<VerificationRequestId>
{
    public const int MaximumAttemptsAllowed = 5;
    public const int MaximumOtpHashLength = 2048;

    private VerificationRequest(
        VerificationRequestId id,
        UserId? userId,
        string target,
        string otpHash,
        VerificationChannel channel,
        VerificationPurpose purpose,
        DateTime createdOnUtc,
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
        MaxAttempts = MaximumAttemptsAllowed;

        CreatedOnUtc = createdOnUtc;
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
        DateTime createdOnUtc,
        DateTime expiresOnUtc)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new DomainException(
                "Verification channel is invalid.");
        }

        if (!Enum.IsDefined(purpose))
        {
            throw new DomainException(
                "Verification purpose is invalid.");
        }

        ValidateChannelAndPurpose(channel, purpose);

        string normalizedTarget = NormalizeTarget(
            target,
            channel);

        string normalizedOtpHash = NormalizeOtpHash(
            otpHash);

        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Creation time must be in UTC.");
        }

        if (expiresOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Expiration time must be in UTC.");
        }

        if (expiresOnUtc <= createdOnUtc)
        {
            throw new DomainException(
                "Expiration time must be after creation time.");
        }

        return new VerificationRequest(
            VerificationRequestId.New(),
            userId,
            normalizedTarget,
            normalizedOtpHash,
            channel,
            purpose,
            createdOnUtc,
            expiresOnUtc);
    }
    public void Verify(DateTime utcNow)
    {
        EnsurePending();

        if (IsExpired(utcNow))
        {
            throw new DomainException(
                "Verification request has expired.");
        }

        Status = VerificationStatus.Verified;
        VerifiedOnUtc = utcNow;

        RaiseDomainEvent(
            new VerificationRequestVerifiedDomainEvent(Id));
    }

    public void RegisterFailedAttempt(DateTime utcNow)
    {
        EnsurePending();

        if (IsExpired(utcNow))
        {
            throw new DomainException(
                "Verification request has expired.");
        }

        Attempts++;

        if (Attempts >= MaxAttempts)
        {
            Invalidate(utcNow);
        }
    }

    public void Invalidate(DateTime utcNow)
    {
        EnsurePending();

        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Invalidation time must be in UTC."
            );
        }

        Status = VerificationStatus.Invalidated;
        InvalidatedOnUtc = utcNow;

        RaiseDomainEvent(
            new VerificationRequestInvalidatedDomainEvent(Id));
    }

    public void MarkExpired(DateTime utcNow)
    {
        EnsurePending();

        if (!IsExpired(utcNow))
        {
            throw new DomainException(
                "Verification request has not expired yet."
            );
        }

        Status = VerificationStatus.Expired;
    }

    public bool IsExpired(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Current time must be in UTC."
            );
        }

        return utcNow >= ExpiresOnUtc;
    }

    private void EnsurePending()
    {
        if (Status != VerificationStatus.Pending)
        {
            throw new DomainException(
                "Verification request is no longer pending.");
        }
    }

    private static void ValidateChannelAndPurpose(
        VerificationChannel channel,
        VerificationPurpose purpose)
    {
        bool isCompatible =
            purpose switch
            {
                VerificationPurpose.ElderlyLogin =>
                    channel ==
                    VerificationChannel.Sms,

                VerificationPurpose.VerifyPhone =>
                    channel ==
                    VerificationChannel.Sms,

                VerificationPurpose.VerifyEmail =>
                    channel ==
                    VerificationChannel.Email,

                VerificationPurpose.ResetPassword =>
                    channel ==
                    VerificationChannel.Email,

                VerificationPurpose.ConfirmExternalLoginLink =>
                    channel ==
                    VerificationChannel.Email,

                _ => false
            };

        if (!isCompatible)
        {
            throw new DomainException(
                "Verification channel does not support " +
                "the requested purpose.");
        }
    }

    private static string NormalizeTarget(
        string target,
        VerificationChannel channel)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new DomainException(
                "Verification target is required.");
        }

        return channel switch
        {
            VerificationChannel.Email =>
                Email.Create(target).Value,

            VerificationChannel.Sms =>
                PhoneNumber.Create(target).Value,

            _ => throw new DomainException(
                "Verification channel is invalid.")
        };
    }

    private static string NormalizeOtpHash(
        string otpHash)
    {
        if (string.IsNullOrWhiteSpace(otpHash))
        {
            throw new DomainException(
                "OTP hash is required.");
        }

        string normalizedHash =
            otpHash.Trim();

        if (normalizedHash.Length >
            MaximumOtpHashLength)
        {
            throw new DomainException(
                $"OTP hash cannot exceed " +
                $"{MaximumOtpHashLength} characters.");
        }

        return normalizedHash;
    }
}