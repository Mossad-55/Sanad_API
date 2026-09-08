using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Identity.Users;

public sealed class UserAvatarCommandTests
{
    [Fact]
    public async Task Upsert_ShouldStoreAvatarPath()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserAsync(
                dbContext);

        FakeFileStorage fileStorage =
            new();

        UpsertUserAvatarCommandHandler handler =
            new(
                dbContext,
                fileStorage);

        Result result =
            await handler.Handle(
                new UpsertUserAvatarCommand(
                    user.Id,
                    "private/avatars/one.jpg"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            "private/avatars/one.jpg",
            user.AvatarUrl);

        Assert.Empty(fileStorage.DeletedKeys);
    }

    [Fact]
    public async Task Upsert_ShouldDeletePreviousFileOnReplace()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserAsync(
                dbContext);

        FakeFileStorage fileStorage =
            new();

        UpsertUserAvatarCommandHandler handler =
            new(
                dbContext,
                fileStorage);

        await handler.Handle(
            new UpsertUserAvatarCommand(
                user.Id,
                "private/avatars/one.jpg"),
            CancellationToken.None);

        Result result =
            await handler.Handle(
                new UpsertUserAvatarCommand(
                    user.Id,
                    "private/avatars/two.jpg"),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            "private/avatars/two.jpg",
            user.AvatarUrl);

        Assert.Equal(
            ["private/avatars/one.jpg"],
            fileStorage.DeletedKeys);
    }

    [Fact]
    public async Task Upsert_ShouldRejectElderlyAccount()
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
                new DateTime(
                    2026,
                    8,
                    20,
                    10,
                    0,
                    0,
                    DateTimeKind.Utc));

        dbContext.Users.Add(user);

        await dbContext.SaveChangesAsync();

        UpsertUserAvatarCommandHandler handler =
            new(
                dbContext,
                new FakeFileStorage());

        Result result =
            await handler.Handle(
                new UpsertUserAvatarCommand(
                    user.Id,
                    "private/avatars/one.jpg"),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            AvatarErrors.UnsupportedAccountType,
            result.Error);

        Assert.Null(user.AvatarUrl);
    }

    [Fact]
    public async Task Upsert_ShouldRejectBlockedUser()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserAsync(
                dbContext);

        user.Block(
            "Blocked for test.",
            new DateTime(
                2026,
                8,
                20,
                10,
                0,
                0,
                DateTimeKind.Utc));

        await dbContext.SaveChangesAsync();

        UpsertUserAvatarCommandHandler handler =
            new(
                dbContext,
                new FakeFileStorage());

        Result result =
            await handler.Handle(
                new UpsertUserAvatarCommand(
                    user.Id,
                    "private/avatars/one.jpg"),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            AvatarErrors.InvalidOperation,
            result.Error);

        Assert.Null(user.AvatarUrl);
    }

    [Fact]
    public async Task Get_ShouldReturnNotFoundWhenMissing()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserAsync(
                dbContext);

        GetUserAvatarQueryHandler handler =
            new(
                dbContext,
                new FakeFileStorage());

        Result<UserAvatarFileContent> result =
            await handler.Handle(
                new GetUserAvatarQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            AvatarErrors.NotFound,
            result.Error);
    }

    [Fact]
    public async Task Get_ShouldReturnStoredFile()
    {
        await using IdentityDbContext dbContext =
            CreateDbContext();

        User user =
            await SeedFamilyUserAsync(
                dbContext);

        user.ChangeAvatar(
            "private/avatars/one.jpg");

        await dbContext.SaveChangesAsync();

        FakeFileStorage fileStorage =
            new();

        GetUserAvatarQueryHandler handler =
            new(
                dbContext,
                fileStorage);

        Result<UserAvatarFileContent> result =
            await handler.Handle(
                new GetUserAvatarQuery(
                    user.Id),
                CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            "image/jpeg",
            result.Value.ContentType);

        Assert.Contains(
            "avatar-",
            result.Value.FileName,
            StringComparison.Ordinal);

        Assert.Equal(
            ["private/avatars/one.jpg"],
            fileStorage.OpenedKeys);

        await result.Value.Content.DisposeAsync();
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

    private sealed class FakeFileStorage :
        IFileStorage
    {
        internal List<string> DeletedKeys { get; } =
            [];

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

            return Task.FromResult<Result<PrivateFileContent>>(
                new PrivateFileContent(
                    key,
                    "image/jpeg",
                    new MemoryStream([1, 2, 3])));
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
