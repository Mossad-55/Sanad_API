using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.UserIdentityDocument;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Identity.Users;

public sealed class IdentityDocumentAdminCommandTests
{
    [Fact]
    public async Task Verify_ShouldMarkPendingDocumentVerified()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserWithDocumentAsync(
                dbContext);

        VerifyIdentityDocumentCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new VerifyIdentityDocumentCommand(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Verified,
            user.IdentityDocument!
                .VerificationStatus);

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);
    }

    [Fact]
    public async Task Verify_ShouldRejectAlreadyVerifiedDocument()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserWithDocumentAsync(
                dbContext);

        VerifyIdentityDocumentCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        await handler.Handle(
            new VerifyIdentityDocumentCommand(
                user.Id),
            CancellationToken.None);

        Result result =
            await handler.Handle(
                new VerifyIdentityDocumentCommand(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            IdentityDocumentErrors.InvalidOperation,
            result.Error);
    }

    [Fact]
    public async Task Reject_ShouldStoreReasonWithoutBlockingUser()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserWithDocumentAsync(
                dbContext);

        RejectIdentityDocumentCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new RejectIdentityDocumentCommand(
                    user.Id,
                    "  Front image is unclear.  "),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Rejected,
            user.IdentityDocument!
                .VerificationStatus);

        Assert.Equal(
            "Front image is unclear.",
            user.IdentityDocument.ReviewReason);

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);
    }

    [Fact]
    public async Task Revoke_ShouldBlockUserAndRevokeSessions()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveFamilyUserWithVerifiedDocumentAsync(
                dbContext);

        DeviceSession session =
            DeviceSession.Create(
                user.Id,
                "iPhone 16",
                DevicePlatform.iOS,
                "1.0.0",
                "refresh-token-hash",
                FixedDateTimeProvider.UtcNowValue,
                FixedDateTimeProvider.UtcNowValue
                    .AddDays(30));

        dbContext.DeviceSessions.Add(session);

        await dbContext.SaveChangesAsync();

        RevokeIdentityDocumentCommandHandler handler =
            new(
                dbContext,
                new FixedDateTimeProvider());

        Result result =
            await handler.Handle(
                new RevokeIdentityDocumentCommand(
                    user.Id,
                    "Identity fraud detected."),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Revoked,
            user.IdentityDocument!
                .VerificationStatus);

        Assert.Equal(
            UserStatus.Blocked,
            user.Status);

        Assert.True(session.IsRevoked);

        Assert.Equal(
            "National ID was revoked.",
            session.RevocationReason);
    }

    [Fact]
    public async Task List_ShouldReturnUploadedDocuments()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserWithDocumentAsync(
                dbContext);

        GetAdminIdentityDocumentsQueryHandler handler =
            new(dbContext);

        Result<PagedIdentityDocumentList> result =
            await handler.Handle(
                new GetAdminIdentityDocumentsQuery(
                    1,
                    10,
                    null),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(1, result.Value.TotalCount);

        AdminIdentityDocumentListItem item =
            Assert.Single(result.Value.Items);

        Assert.Equal(
            user.Id.Value,
            item.UserId);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Pending,
            item.VerificationStatus);

        Assert.Equal(
            "Mohamed Ahmed",
            item.EnglishFullName);
    }

    [Fact]
    public async Task Get_ShouldReturnDetailWithoutFilePaths()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserWithDocumentAsync(
                dbContext);

        GetAdminIdentityDocumentQueryHandler handler =
            new(dbContext);

        Result<AdminIdentityDocumentDetail> result =
            await handler.Handle(
                new GetAdminIdentityDocumentQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            user.Id.Value,
            result.Value.UserId);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Pending,
            result.Value.VerificationStatus);

        Assert.Null(result.Value.ReviewReason);
    }

    [Fact]
    public async Task DownloadFront_ShouldReturnStoredFile()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserWithDocumentAsync(
                dbContext);

        FakeFileStorage fileStorage =
            new();

        GetIdentityDocumentFileQueryHandler handler =
            new(
                dbContext,
                fileStorage);

        Result<IdentityDocumentFileContent> result =
            await handler.Handle(
                new GetIdentityDocumentFileQuery(
                    user.Id,
                    IdentityDocumentFileSide.Front),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            "image/jpeg",
            result.Value.ContentType);

        Assert.Contains(
            "front",
            result.Value.FileName,
            StringComparison.Ordinal);

        Assert.Equal(
            "private/identity-documents/front-1.jpg",
            fileStorage.OpenedKeys.Single());

        await result.Value.Content.DisposeAsync();
    }

    [Fact]
    public async Task Get_ShouldReturnNotFoundWhenDocumentMissing()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserAsync(
                dbContext);

        GetAdminIdentityDocumentQueryHandler handler =
            new(dbContext);

        Result<AdminIdentityDocumentDetail> result =
            await handler.Handle(
                new GetAdminIdentityDocumentQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            IdentityDocumentErrors.NotFound,
            result.Error);
    }

    private static async Task<User> SeedFamilyUserWithDocumentAsync(
        IdentityDbContext dbContext)
    {
        User user =
            await SeedFamilyUserAsync(
                dbContext);

        user.UploadIdentityDocument(
            "private/identity-documents/front-1.jpg",
            "private/identity-documents/back-1.jpg",
            FixedDateTimeProvider.UtcNowValue);

        await dbContext.SaveChangesAsync();

        return user;
    }

    private static async Task<User> SeedActiveFamilyUserWithVerifiedDocumentAsync(
        IdentityDbContext dbContext)
    {
        User user =
            await SeedFamilyUserWithDocumentAsync(
                dbContext);

        user.SetInitialPasswordHash(
            "password-hash",
            FixedDateTimeProvider.UtcNowValue);

        user.VerifyEmail(
            FixedDateTimeProvider.UtcNowValue);

        user.VerifyPhone(
            FixedDateTimeProvider.UtcNowValue);

        user.Activate(
            FixedDateTimeProvider.UtcNowValue);

        user.VerifyIdentityDocument(
            FixedDateTimeProvider.UtcNowValue);

        await dbContext.SaveChangesAsync();

        return user;
    }

    private static async Task<User> SeedFamilyUserAsync(
        IdentityDbContext dbContext)
    {
        User user =
            User.Create(
                FullName.Create("محمد أحمد"),
                FullName.Create("Mohamed Ahmed"),
                Email.Create("mohamed@example.com"),
                PhoneNumber.Create("+201001234567"));

        user.AddAccount(
            AccountType.Family);

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        return user;
    }

    private static IdentityDbContext CreateDbContext()
    {
        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        IdentityDbContext dbContext =
            new(options);

        dbContext.Database.EnsureCreated();

        return dbContext;
    }

    private sealed class FixedDateTimeProvider :
        IDateTimeProvider
    {
        internal static readonly DateTime UtcNowValue =
            new(
                2026,
                8,
                20,
                10,
                0,
                0,
                DateTimeKind.Utc);

        public DateTime UtcNow =>
            UtcNowValue;
    }

    private sealed class FakeFileStorage :
        IFileStorage
    {
        internal List<string> OpenedKeys { get; } =
            [];

        public Task<Result<StoredFile>> SaveAsync(
            Stream content,
            string contentType,
            long contentLength,
            string folder,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Result<StoredFile>>(
                new StoredFile("unused"));
        }

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content,
            string contentType,
            long contentLength,
            string folder,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Result<StoredFile>>(
                new StoredFile("unused"));
        }

        public Task<Result<PrivateFileContent>> OpenReadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            OpenedKeys.Add(key);

            Stream content =
                new MemoryStream(
                    [1, 2, 3]);

            return Task.FromResult<Result<PrivateFileContent>>(
                new PrivateFileContent(
                    key,
                    "image/jpeg",
                    content));
        }

        public Task<Result> DeleteAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                Result.Success());
        }
    }
}
