using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Account;

public sealed class AnonymizeFamilyAccountsCommandHandlerTests
{
    [Fact]
    public async Task AnonymizeFamilyAccounts_BlocksAndScrubs_PureFamilyUser()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedFamilyUserAsync(dbContext, withAvatar: true);

        DeviceSession session = CreateSession(user.Id, FixedDateTimeProvider.UtcNowValue);
        dbContext.DeviceSessions.Add(session);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        string originalPhone = user.PhoneNumber.Value;

        var handler = CreateHandler(dbContext);

        var command = new AnonymizeFamilyAccountsCommand(new List<UserId> { user.Id });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(UserStatus.Blocked, user.Status);
        Assert.NotNull(user.StatusReason);

        Assert.Equal("محذوف", user.ArabicFullName.Value);
        Assert.Equal("Deleted", user.EnglishFullName.Value);
        Assert.Null(user.Email);
        Assert.False(user.EmailVerified);
        Assert.False(user.PhoneVerified);
        Assert.StartsWith("+200", user.PhoneNumber.Value);
        Assert.NotEqual(originalPhone, user.PhoneNumber.Value);
        Assert.Null(user.AvatarUrl);

        Assert.NotNull(session.RevokedOnUtc);
    }

    [Fact]
    public async Task AnonymizeFamilyAccounts_Skips_CaregiverHybridUser()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedHybridUserAsync(dbContext);

        DeviceSession session = CreateSession(user.Id, FixedDateTimeProvider.UtcNowValue);
        dbContext.DeviceSessions.Add(session);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        string originalArabic = user.ArabicFullName.Value;
        string originalEnglish = user.EnglishFullName.Value;
        string? originalEmail = user.Email?.Value;
        string originalPhone = user.PhoneNumber.Value;

        var handler = CreateHandler(dbContext);

        var command = new AnonymizeFamilyAccountsCommand(new List<UserId> { user.Id });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(originalArabic, user.ArabicFullName.Value);
        Assert.Equal(originalEnglish, user.EnglishFullName.Value);
        Assert.Equal(originalEmail, user.Email?.Value);
        Assert.Equal(originalPhone, user.PhoneNumber.Value);
        Assert.Null(session.RevokedOnUtc);
    }

    [Fact]
    public async Task AnonymizeFamilyAccounts_IsIdempotent_SecondRunLeavesSameState()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedFamilyUserAsync(dbContext, withAvatar: false);

        DeviceSession session = CreateSession(user.Id, FixedDateTimeProvider.UtcNowValue);
        dbContext.DeviceSessions.Add(session);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        var handler = CreateHandler(dbContext);
        var command = new AnonymizeFamilyAccountsCommand(new List<UserId> { user.Id });

        var result1 = await handler.Handle(command, CancellationToken.None);
        Assert.True(result1.IsSuccess);

        string phoneAfterFirst = user.PhoneNumber.Value;
        UserStatus statusAfterFirst = user.Status;
        DateTime? revokedAfterFirst = session.RevokedOnUtc;

        var result2 = await handler.Handle(command, CancellationToken.None);
        Assert.True(result2.IsSuccess);

        Assert.Equal(statusAfterFirst, user.Status);
        Assert.Equal(phoneAfterFirst, user.PhoneNumber.Value);
        Assert.Equal(UserStatus.Blocked, user.Status);
        Assert.Equal(revokedAfterFirst, session.RevokedOnUtc);
    }

    [Fact]
    public async Task AnonymizeFamilyAccounts_ReturnsUserNotFound_WhenARequestedUserIsMissing()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User existingUser = await SeedFamilyUserAsync(dbContext, withAvatar: false);

        string originalArabic = existingUser.ArabicFullName.Value;
        UserStatus originalStatus = existingUser.Status;

        dbContext.ResetSaveChangesCalls();

        var handler = CreateHandler(dbContext);

        UserId missingId = UserId.New();

        var command = new AnonymizeFamilyAccountsCommand(new List<UserId> { existingUser.Id, missingId });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.UserNotFound, result.Error);

        Assert.Equal(originalArabic, existingUser.ArabicFullName.Value);
        Assert.Equal(originalStatus, existingUser.Status);
    }

    [Fact]
    public async Task AnonymizeFamilyAccounts_TreatsElderlyOnlyUser_Fully()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User elderlyUser = await SeedElderlyUserAsync(dbContext);

        DeviceSession session = CreateSession(elderlyUser.Id, FixedDateTimeProvider.UtcNowValue);
        dbContext.DeviceSessions.Add(session);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        var handler = CreateHandler(dbContext);

        var command = new AnonymizeFamilyAccountsCommand(new List<UserId> { elderlyUser.Id });

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(UserStatus.Blocked, elderlyUser.Status);
        Assert.Equal("محذوف", elderlyUser.ArabicFullName.Value);
        Assert.Equal("Deleted", elderlyUser.EnglishFullName.Value);
        Assert.Null(elderlyUser.Email);
        Assert.StartsWith("+200", elderlyUser.PhoneNumber.Value);
        Assert.NotNull(session.RevokedOnUtc);
    }

    private static AnonymizeFamilyAccountsCommandHandler CreateHandler(
        IdentityTestDbContext dbContext)
    {
        return new AnonymizeFamilyAccountsCommandHandler(
            dbContext,
            new FixedDateTimeProvider());
    }

    private static IdentityTestDbContext CreateDbContext()
    {
        DbContextOptions<IdentityTestDbContext> options =
            new DbContextOptionsBuilder<IdentityTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new IdentityTestDbContext(options);
    }

    private static async Task<User> SeedFamilyUserAsync(
        IdentityTestDbContext dbContext,
        bool withAvatar)
    {
        User user = User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create("mohamed@example.com"),
            PhoneNumber.Create("+201001234567"),
            withAvatar ? "avatar_key" : null);

        user.AddAccount(AccountType.Family);

        user.SetInitialPasswordHash(
            "hashed-password",
            FixedDateTimeProvider.UtcNowValue);

        user.VerifyEmail(FixedDateTimeProvider.UtcNowValue);
        user.VerifyPhone(FixedDateTimeProvider.UtcNowValue);
        user.Activate(FixedDateTimeProvider.UtcNowValue.AddMinutes(1));

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private static async Task<User> SeedHybridUserAsync(
        IdentityTestDbContext dbContext)
    {
        User user = User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create("hybrid@example.com"),
            PhoneNumber.Create("+201001234568"));

        user.AddAccount(AccountType.Family);
        user.AddAccount(AccountType.MedicalCaregiver);

        user.SetInitialPasswordHash(
            "hashed-password",
            FixedDateTimeProvider.UtcNowValue);

        user.VerifyEmail(FixedDateTimeProvider.UtcNowValue);
        user.VerifyPhone(FixedDateTimeProvider.UtcNowValue);
        user.Activate(FixedDateTimeProvider.UtcNowValue.AddMinutes(1));

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private static async Task<User> SeedElderlyUserAsync(
        IdentityTestDbContext dbContext)
    {
        User user = User.CreateElderly(
            FullName.Create("جد"),
            FullName.Create("Grandfather"),
            PhoneNumber.Create("+201009876543"),
            Sanad.BuildingBlocks.Domain.Enums.Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            FixedDateTimeProvider.UtcNowValue);

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private static DeviceSession CreateSession(
        UserId userId,
        DateTime createdOnUtc)
    {
        return DeviceSession.Create(
            userId,
            "iPhone 16",
            DevicePlatform.iOS,
            "1.0.0",
            "refresh-token-hash",
            createdOnUtc,
            createdOnUtc.AddDays(30));
    }

    private sealed class FixedDateTimeProvider : IDateTimeProvider
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

        public DateTime UtcNow => UtcNowValue;
    }
}
