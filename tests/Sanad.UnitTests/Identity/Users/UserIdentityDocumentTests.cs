using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.UserIdentityDocument;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Users.Events;

namespace Sanad.UnitTests.Identity.Users;

public sealed class UserIdentityDocumentTests
{
    [Fact]
    public void UploadIdentityDocument_ShouldCreatePendingDocument()
    {
        User user =
            CreateUser();

        DateTime uploadedOnUtc =
            CreateUtcDateTime();

        user.UploadIdentityDocument(
            "  identity/front.jpg  ",
            "  identity/back.jpg  ",
            uploadedOnUtc);

        UserIdentityDocument document =
            Assert.IsType<UserIdentityDocument>(
                user.IdentityDocument);

        Assert.NotEqual(
            UserIdentityDocumentId.Empty,
            document.Id);

        Assert.Equal(
            "identity/front.jpg",
            document.FrontImagePath);

        Assert.Equal(
            "identity/back.jpg",
            document.BackImagePath);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Pending,
            document.VerificationStatus);

        Assert.Null(document.ReviewReason);

        Assert.Equal(
            uploadedOnUtc,
            document.CreatedOnUtc);

        Assert.Equal(
            uploadedOnUtc,
            document.UpdatedOnUtc);

        Assert.Equal(
            uploadedOnUtc,
            user.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(null, "identity/back.jpg")]
    [InlineData("", "identity/back.jpg")]
    [InlineData("   ", "identity/back.jpg")]
    [InlineData("identity/front.jpg", null)]
    [InlineData("identity/front.jpg", "")]
    [InlineData("identity/front.jpg", "   ")]
    public void UploadIdentityDocument_ShouldRejectMissingImage(
        string? frontImagePath,
        string? backImagePath)
    {
        User user =
            CreateUser();

        Assert.Throws<DomainException>(
            () => user.UploadIdentityDocument(
                frontImagePath!,
                backImagePath!,
                CreateUtcDateTime()));

        Assert.Null(user.IdentityDocument);
    }

    [Fact]
    public void UploadIdentityDocument_ShouldRejectDuplicateDocument()
    {
        User user =
            CreateUser();

        user.UploadIdentityDocument(
            "identity/front.jpg",
            "identity/back.jpg",
            CreateUtcDateTime());

        UserIdentityDocument originalDocument =
            user.IdentityDocument!;

        Assert.Throws<DomainException>(
            () => user.UploadIdentityDocument(
                "identity/new-front.jpg",
                "identity/new-back.jpg",
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Same(
            originalDocument,
            user.IdentityDocument);
    }

    [Fact]
    public void VerifyIdentityDocument_ShouldVerifyPendingDocument()
    {
        User user =
            CreateUserWithDocument();

        DateTime verifiedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(1);

        user.VerifyIdentityDocument(
            verifiedOnUtc);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Verified,
            user.IdentityDocument!
                .VerificationStatus);

        Assert.Null(
            user.IdentityDocument
                .ReviewReason);

        Assert.Equal(
            verifiedOnUtc,
            user.IdentityDocument
                .UpdatedOnUtc);
    }

    [Fact]
    public void VerifyIdentityDocument_ShouldRejectNonPendingDocument()
    {
        User user =
            CreateUserWithDocument();

        user.VerifyIdentityDocument(
            CreateUtcDateTime()
                .AddMinutes(1));

        Assert.Throws<DomainException>(
            () => user.VerifyIdentityDocument(
                CreateUtcDateTime()
                    .AddMinutes(2)));

        Assert.Equal(
            IdentityDocumentVerificationStatus.Verified,
            user.IdentityDocument!
                .VerificationStatus);
    }

    [Fact]
    public void RejectIdentityDocument_ShouldStoreNormalizedReason()
    {
        User user =
            CreateUserWithDocument();

        user.RejectIdentityDocument(
            "  Front image is unclear.  ",
            CreateUtcDateTime()
                .AddMinutes(1));

        Assert.Equal(
            IdentityDocumentVerificationStatus.Rejected,
            user.IdentityDocument!
                .VerificationStatus);

        Assert.Equal(
            "Front image is unclear.",
            user.IdentityDocument
                .ReviewReason);
    }

    [Fact]
    public void UpdateIdentityDocument_ShouldReturnRejectedDocumentToPending()
    {
        User user =
            CreateUserWithDocument();

        UserIdentityDocument document =
            user.IdentityDocument!;

        UserIdentityDocumentId originalId =
            document.Id;

        DateTime originalCreatedOnUtc =
            document.CreatedOnUtc;

        user.RejectIdentityDocument(
            "Images are unclear.",
            CreateUtcDateTime()
                .AddMinutes(1));

        user.UpdateIdentityDocument(
            "identity/new-front.jpg",
            "identity/new-back.jpg",
            CreateUtcDateTime()
                .AddMinutes(2));

        Assert.Equal(
            originalId,
            document.Id);

        Assert.Equal(
            originalCreatedOnUtc,
            document.CreatedOnUtc);

        Assert.Equal(
            "identity/new-front.jpg",
            document.FrontImagePath);

        Assert.Equal(
            "identity/new-back.jpg",
            document.BackImagePath);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Pending,
            document.VerificationStatus);

        Assert.Null(document.ReviewReason);
    }

    [Fact]
    public void UpdateIdentityDocument_ShouldReturnActiveUserToPendingVerification()
    {
        User user =
            CreateActiveFamilyUserWithVerifiedDocument();

        user.ClearDomainEvents();

        DateTime updatedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(3);

        user.UpdateIdentityDocument(
            "identity/new-front.jpg",
            "identity/new-back.jpg",
            updatedOnUtc);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Pending,
            user.IdentityDocument!
                .VerificationStatus);

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);

        Assert.Equal(
            updatedOnUtc,
            user.UpdatedOnUtc);

        UserStatusChangedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserStatusChangedDomainEvent>());

        Assert.Equal(
            UserStatus.Active,
            domainEvent.PreviousStatus);

        Assert.Equal(
            UserStatus.PendingVerification,
            domainEvent.CurrentStatus);
    }

    [Fact]
    public void RevokeIdentityDocument_ShouldBlockActiveUser()
    {
        User user =
            CreateActiveFamilyUserWithVerifiedDocument();

        user.ClearDomainEvents();

        DateTime revokedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(3);

        user.RevokeIdentityDocument(
            "  Identity fraud detected.  ",
            revokedOnUtc);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Revoked,
            user.IdentityDocument!
                .VerificationStatus);

        Assert.Equal(
            "Identity fraud detected.",
            user.IdentityDocument
                .ReviewReason);

        Assert.Equal(
            UserStatus.Blocked,
            user.Status);

        Assert.Equal(
            "Identity fraud detected.",
            user.StatusReason);

        UserStatusChangedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserStatusChangedDomainEvent>());

        Assert.Equal(
            UserStatus.Active,
            domainEvent.PreviousStatus);

        Assert.Equal(
            UserStatus.Blocked,
            domainEvent.CurrentStatus);
    }

    [Fact]
    public void RevokeIdentityDocument_ShouldRejectPendingDocument()
    {
        User user =
            CreateUserWithDocument();

        Assert.Throws<DomainException>(
            () => user.RevokeIdentityDocument(
                "Invalid document.",
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            IdentityDocumentVerificationStatus.Pending,
            user.IdentityDocument!
                .VerificationStatus);

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);
    }

    [Fact]
    public void UpdateIdentityDocument_ShouldRejectBlockedUser()
    {
        User user =
            CreateActiveFamilyUserWithVerifiedDocument();

        user.RevokeIdentityDocument(
            "Identity fraud detected.",
            CreateUtcDateTime()
                .AddMinutes(3));

        Assert.Throws<DomainException>(
            () => user.UpdateIdentityDocument(
                "identity/new-front.jpg",
                "identity/new-back.jpg",
                CreateUtcDateTime()
                    .AddMinutes(4)));

        Assert.Equal(
            IdentityDocumentVerificationStatus.Revoked,
            user.IdentityDocument!
                .VerificationStatus);
    }

    [Fact]
    public void UnblockThenUpdate_ShouldReturnRevokedDocumentToPending()
    {
        User user =
            CreateActiveFamilyUserWithVerifiedDocument();

        user.RevokeIdentityDocument(
            "Identity fraud detected.",
            CreateUtcDateTime()
                .AddMinutes(3));

        user.Unblock(
            CreateUtcDateTime()
                .AddMinutes(4));

        user.UpdateIdentityDocument(
            "identity/new-front.jpg",
            "identity/new-back.jpg",
            CreateUtcDateTime()
                .AddMinutes(5));

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Pending,
            user.IdentityDocument!
                .VerificationStatus);

        Assert.Null(
            user.IdentityDocument
                .ReviewReason);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void UploadIdentityDocument_ShouldRejectNonUtcTime(
        DateTimeKind dateTimeKind)
    {
        User user =
            CreateUser();

        DateTime invalidTime =
            DateTime.SpecifyKind(
                CreateUtcDateTime(),
                dateTimeKind);

        Assert.Throws<DomainException>(
            () => user.UploadIdentityDocument(
                "identity/front.jpg",
                "identity/back.jpg",
                invalidTime));

        Assert.Null(user.IdentityDocument);
    }

    [Fact]
    public void DocumentOperations_ShouldRejectMissingDocument()
    {
        User user =
            CreateUser();

        Assert.Throws<DomainException>(
            () => user.VerifyIdentityDocument(
                CreateUtcDateTime()));

        Assert.Throws<DomainException>(
            () => user.RejectIdentityDocument(
                "Invalid document.",
                CreateUtcDateTime()));

        Assert.Throws<DomainException>(
            () => user.UpdateIdentityDocument(
                "identity/front.jpg",
                "identity/back.jpg",
                CreateUtcDateTime()));
    }

    private static User CreateUserWithDocument()
    {
        User user =
            CreateUser();

        user.UploadIdentityDocument(
            "identity/front.jpg",
            "identity/back.jpg",
            CreateUtcDateTime());

        return user;
    }

    private static User
        CreateActiveFamilyUserWithVerifiedDocument()
    {
        User user =
            CreateUserWithDocument();

        user.VerifyIdentityDocument(
            CreateUtcDateTime()
                .AddMinutes(1));

        user.AddAccount(
            AccountType.Family);

        user.VerifyEmail(
            CreateUtcDateTime());

        user.VerifyPhone(
            CreateUtcDateTime());

        user.SetInitialPasswordHash(
            "password-hash",
            CreateUtcDateTime());

        user.Activate(
            CreateUtcDateTime()
                .AddMinutes(2));

        return user;
    }

    private static User CreateUser()
    {
        return User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create(
                "mohamed@example.com"),
            PhoneNumber.Create(
                "+201001234567"));
    }

    private static DateTime CreateUtcDateTime()
    {
        return new DateTime(
            2026,
            8,
            20,
            10,
            0,
            0,
            DateTimeKind.Utc);
    }
}