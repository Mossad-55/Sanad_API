using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.UserIdentityDocument;

namespace Sanad.Modules.Identity.Domain.Users;

public sealed class UserIdentityDocument : Entity<UserIdentityDocumentId>
{
    private UserIdentityDocument()
    {
    }

    private UserIdentityDocument(
        UserIdentityDocumentId id,
        string frontImagePath,
        string backImagePath)
        : base(id)
    {
        FrontImagePath = frontImagePath;
        BackImagePath = backImagePath;

        VerificationStatus =
            IdentityDocumentVerificationStatus.Pending;

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public string FrontImagePath { get; private set; } = string.Empty;
    public string BackImagePath { get; private set; } = string.Empty;
    public IdentityDocumentVerificationStatus VerificationStatus { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static UserIdentityDocument Create(
        string frontImagePath,
        string backImagePath)
    {
        return new(
            UserIdentityDocumentId.New(),
            frontImagePath,
            backImagePath);
    }

    public void UpdateImages(
        string frontImagePath,
        string backImagePath)
    {
        FrontImagePath = frontImagePath;
        BackImagePath = backImagePath;

        VerificationStatus =
            IdentityDocumentVerificationStatus.Pending;

        RejectionReason = null;

        UpdatedOnUtc = DateTime.UtcNow;
    }
    public void Verify()
    {
        VerificationStatus =
            IdentityDocumentVerificationStatus.Verified;

        RejectionReason = null;

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Reject(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("Rejection reason is required.");
        }

        VerificationStatus =
            IdentityDocumentVerificationStatus.Rejected;

        RejectionReason = reason.Trim();

        UpdatedOnUtc = DateTime.UtcNow;
    }
}