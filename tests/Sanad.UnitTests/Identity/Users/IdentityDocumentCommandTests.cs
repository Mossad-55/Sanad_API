using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.UserIdentityDocument;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Identity.Users;

public sealed class IdentityDocumentCommandTests
{
    [Fact]
    public async Task Handle_ShouldUploadPendingDocument()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserAsync(
                dbContext);

        FakeFileStorage fileStorage =
            new();

        UpsertIdentityDocumentCommandHandler handler =
            CreateUpsertHandler(
                dbContext,
                fileStorage);

        Result<IdentityDocumentResponse> result =
            await handler.Handle(
                new UpsertIdentityDocumentCommand(
                    user.Id,
                    "private/identity-documents/front-1.jpg",
                    "private/identity-documents/back-1.jpg"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.True(result.Value.Uploaded);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Pending,
            result.Value.VerificationStatus);

        Assert.Null(result.Value.ReviewReason);

        Assert.Equal(
            "private/identity-documents/front-1.jpg",
            user.IdentityDocument!.FrontImagePath);

        Assert.Equal(
            "private/identity-documents/back-1.jpg",
            user.IdentityDocument.BackImagePath);

        Assert.Empty(fileStorage.DeletedKeys);
    }

    [Fact]
    public async Task Handle_ShouldReplaceExistingDocumentAndDeletePreviousFiles()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserAsync(
                dbContext);

        FakeFileStorage fileStorage =
            new();

        UpsertIdentityDocumentCommandHandler handler =
            CreateUpsertHandler(
                dbContext,
                fileStorage);

        await handler.Handle(
            new UpsertIdentityDocumentCommand(
                user.Id,
                "private/identity-documents/front-1.jpg",
                "private/identity-documents/back-1.jpg"),
            CancellationToken.None);

        Result<IdentityDocumentResponse> result =
            await handler.Handle(
                new UpsertIdentityDocumentCommand(
                    user.Id,
                    "private/identity-documents/front-2.jpg",
                    "private/identity-documents/back-2.jpg"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Pending,
            result.Value.VerificationStatus);

        Assert.Equal(
            "private/identity-documents/front-2.jpg",
            user.IdentityDocument!.FrontImagePath);

        Assert.Equal(
            "private/identity-documents/back-2.jpg",
            user.IdentityDocument.BackImagePath);

        Assert.Equal(
            [
                "private/identity-documents/front-1.jpg",
                "private/identity-documents/back-1.jpg"
            ],
            fileStorage.DeletedKeys);
    }

    [Fact]
    public async Task Handle_ShouldReturnEmptyDocumentWhenNoneUploaded()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserAsync(
                dbContext);

        GetIdentityDocumentQueryHandler handler =
            new(dbContext);

        Result<IdentityDocumentResponse> result =
            await handler.Handle(
                new GetIdentityDocumentQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.False(result.Value.Uploaded);

        Assert.Null(result.Value.VerificationStatus);

        Assert.Null(result.Value.ReviewReason);
    }

    [Fact]
    public async Task Handle_ShouldRejectUnknownUser()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        UpsertIdentityDocumentCommandHandler handler =
            CreateUpsertHandler(
                dbContext,
                new FakeFileStorage());

        Result<IdentityDocumentResponse> result =
            await handler.Handle(
                new UpsertIdentityDocumentCommand(
                    UserId.New(),
                    "private/identity-documents/front-1.jpg",
                    "private/identity-documents/back-1.jpg"),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            IdentityDocumentErrors.UserNotFound,
            result.Error);
    }

    [Fact]
    public async Task Handle_ShouldRejectElderlyAccount()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            User.CreateElderly(
                FullName.Create("محمد أحمد"),
                FullName.Create("Mohamed Ahmed"),
                PhoneNumber.Create("+201001234567"),
                Gender.Male,
                new DateOnly(1948, 1, 1),
                FixedDateTimeProvider.UtcNowValue);

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        UpsertIdentityDocumentCommandHandler handler =
            CreateUpsertHandler(
                dbContext,
                new FakeFileStorage());

        Result<IdentityDocumentResponse> result =
            await handler.Handle(
                new UpsertIdentityDocumentCommand(
                    user.Id,
                    "private/identity-documents/front-1.jpg",
                    "private/identity-documents/back-1.jpg"),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            IdentityDocumentErrors.UnsupportedAccountType,
            result.Error);

        Assert.Null(user.IdentityDocument);
    }

    [Fact]
    public async Task Handle_ShouldRejectBlockedUser()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserAsync(
                dbContext);

        user.Block(
            "Blocked for test.",
            FixedDateTimeProvider.UtcNowValue);

        await dbContext.SaveChangesAsync();

        UpsertIdentityDocumentCommandHandler handler =
            CreateUpsertHandler(
                dbContext,
                new FakeFileStorage());

        Result<IdentityDocumentResponse> result =
            await handler.Handle(
                new UpsertIdentityDocumentCommand(
                    user.Id,
                    "private/identity-documents/front-1.jpg",
                    "private/identity-documents/back-1.jpg"),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            IdentityDocumentErrors.InvalidOperation,
            result.Error);

        Assert.Null(user.IdentityDocument);
    }

    [Fact]
    public async Task Handle_ShouldReturnActiveUserToPendingVerificationAndRevokeSessions()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedActiveFamilyUserAsync(
                dbContext);

        FakeFileStorage fileStorage =
            new();

        UpsertIdentityDocumentCommandHandler handler =
            CreateUpsertHandler(
                dbContext,
                fileStorage);

        await handler.Handle(
            new UpsertIdentityDocumentCommand(
                user.Id,
                "private/identity-documents/front-1.jpg",
                "private/identity-documents/back-1.jpg"),
            CancellationToken.None);

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

        Assert.Equal(
            UserStatus.Active,
            user.Status);

        Result<IdentityDocumentResponse> result =
            await handler.Handle(
                new UpsertIdentityDocumentCommand(
                    user.Id,
                    "private/identity-documents/front-2.jpg",
                    "private/identity-documents/back-2.jpg"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            IdentityDocumentVerificationStatus.Pending,
            result.Value.VerificationStatus);

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);

        Assert.True(session.IsRevoked);

        Assert.Equal(
            "National ID was replaced.",
            session.RevocationReason);

        Assert.Equal(
            FixedDateTimeProvider.UtcNowValue,
            session.RevokedOnUtc);
    }

    private static UpsertIdentityDocumentCommandHandler CreateUpsertHandler(
        IdentityDbContext dbContext,
        IFileStorage fileStorage)
    {
        return new UpsertIdentityDocumentCommandHandler(
            dbContext,
            fileStorage,
            new FixedDateTimeProvider());
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

    private static async Task<User> SeedActiveFamilyUserAsync(
        IdentityDbContext dbContext)
    {
        User user =
            await SeedFamilyUserAsync(
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
        internal List<string> DeletedKeys { get; } =
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
            return Task.FromResult(
                Result<PrivateFileContent>.Failure(
                    StorageErrors.NotFound));
        }

        public Task<Result> DeleteAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);

            return Task.FromResult(
                Result.Success());
        }
    }
}
