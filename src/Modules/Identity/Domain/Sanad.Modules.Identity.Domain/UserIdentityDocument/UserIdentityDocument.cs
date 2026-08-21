using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.UserIdentityDocument;

namespace Sanad.Modules.Identity.Domain.Users;

public sealed class UserIdentityDocument :
    Entity<UserIdentityDocumentId>
{
    public const int MaximumImagePathLength = 500;
    public const int MaximumReviewReasonLength = 1000;

    private UserIdentityDocument()
    {
    }

    private UserIdentityDocument(
        UserIdentityDocumentId id,
        string frontImagePath,
        string backImagePath,
        DateTime createdOnUtc)
        : base(id)
    {
        FrontImagePath = frontImagePath;
        BackImagePath = backImagePath;

        VerificationStatus =
            IdentityDocumentVerificationStatus.Pending;

        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public string FrontImagePath { get; private set; } =
        string.Empty;

    public string BackImagePath { get; private set; } =
        string.Empty;

    public IdentityDocumentVerificationStatus
        VerificationStatus
    {
        get;
        private set;
    }

    public string? ReviewReason { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    internal static UserIdentityDocument Create(
        string frontImagePath,
        string backImagePath,
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        string normalizedFrontPath =
            NormalizeImagePath(
                frontImagePath,
                "Front image");

        string normalizedBackPath =
            NormalizeImagePath(
                backImagePath,
                "Back image");

        return new UserIdentityDocument(
            UserIdentityDocumentId.New(),
            normalizedFrontPath,
            normalizedBackPath,
            utcNow);
    }

    internal void UpdateImages(
        string frontImagePath,
        string backImagePath,
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        string normalizedFrontPath =
            NormalizeImagePath(
                frontImagePath,
                "Front image");

        string normalizedBackPath =
            NormalizeImagePath(
                backImagePath,
                "Back image");

        FrontImagePath = normalizedFrontPath;
        BackImagePath = normalizedBackPath;

        VerificationStatus =
            IdentityDocumentVerificationStatus.Pending;

        ReviewReason = null;
        UpdatedOnUtc = utcNow;
    }

    internal void Verify(
        DateTime utcNow)
    {
        EnsurePending(
            "Only a Pending Identity Document " +
            "can be Verified.");

        ValidateUtc(utcNow);

        VerificationStatus =
            IdentityDocumentVerificationStatus.Verified;

        ReviewReason = null;
        UpdatedOnUtc = utcNow;
    }

    internal void Reject(
        string reason,
        DateTime utcNow)
    {
        EnsurePending(
            "Only a Pending Identity Document " +
            "can be Rejected.");

        ValidateUtc(utcNow);

        string normalizedReason =
            NormalizeReviewReason(
                reason,
                "Rejection reason");

        VerificationStatus =
            IdentityDocumentVerificationStatus.Rejected;

        ReviewReason = normalizedReason;
        UpdatedOnUtc = utcNow;
    }

    internal void Revoke(
        string reason,
        DateTime utcNow)
    {
        if (VerificationStatus !=
            IdentityDocumentVerificationStatus.Verified)
        {
            throw new DomainException(
                "Only a Verified Identity Document " +
                "can be Revoked.");
        }

        ValidateUtc(utcNow);

        string normalizedReason =
            NormalizeReviewReason(
                reason,
                "Revocation reason");

        VerificationStatus =
            IdentityDocumentVerificationStatus.Revoked;

        ReviewReason = normalizedReason;
        UpdatedOnUtc = utcNow;
    }

    private void EnsurePending(
        string errorMessage)
    {
        if (VerificationStatus !=
            IdentityDocumentVerificationStatus.Pending)
        {
            throw new DomainException(
                errorMessage);
        }
    }

    private static string NormalizeImagePath(
        string imagePath,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(
            imagePath))
        {
            throw new DomainException(
                $"{fieldName} is required.");
        }

        string normalizedPath =
            imagePath.Trim();

        if (normalizedPath.Length >
            MaximumImagePathLength)
        {
            throw new DomainException(
                $"{fieldName} path cannot exceed " +
                $"{MaximumImagePathLength} characters.");
        }

        return normalizedPath;
    }

    private static string NormalizeReviewReason(
        string reason,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                $"{fieldName} is required.");
        }

        string normalizedReason =
            reason.Trim();

        if (normalizedReason.Length >
            MaximumReviewReasonLength)
        {
            throw new DomainException(
                $"{fieldName} cannot exceed " +
                $"{MaximumReviewReasonLength} characters.");
        }

        return normalizedReason;
    }

    private static void ValidateUtc(
        DateTime utcNow)
    {
        if (utcNow.Kind !=
            DateTimeKind.Utc)
        {
            throw new DomainException(
                "Identity Document operation time " +
                "must be in UTC.");
        }
    }
}