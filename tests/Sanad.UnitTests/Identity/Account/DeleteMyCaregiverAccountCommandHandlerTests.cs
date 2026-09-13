using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Caregivers;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Account;

public sealed class DeleteMyCaregiverAccountCommandHandlerTests
{
    private static readonly CaregiverId CaregiverIdValue =
        CaregiverId.New();

    private const string DeactivationReason =
        "Self-deleted by the account owner.";

    [Fact]
    public async Task DeleteMyCaregiverAccount_AnonymizesUser_DeactivatesCaregiver_RevokesSessions()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedCaregiverUserAsync(dbContext, AccountType.MedicalCaregiver);

        DeviceSession session = CreateSession(user.Id, FixedDateTimeProvider.UtcNowValue);
        dbContext.DeviceSessions.Add(session);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        var gateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = new CaregiverAccountInfo(CaregiverIdValue, IsDeactivated: false)
        };

        var handler = CreateHandler(dbContext, gateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // PURE caregiver → full deletion (anonymize & retain).
        Assert.Equal(UserStatus.Blocked, user.Status);
        Assert.Equal("محذوف", user.ArabicFullName.Value);
        Assert.Equal("Deleted", user.EnglishFullName.Value);
        Assert.Null(user.Email);
        Assert.False(user.EmailVerified);
        Assert.False(user.PhoneVerified);
        Assert.StartsWith("+200", user.PhoneNumber.Value);
        Assert.Null(user.AvatarUrl);

        Assert.NotNull(session.RevokedOnUtc);
        Assert.Equal("The account was deleted by its owner.", session.RevocationReason);

        // The caregiver profile is deactivated through the port.
        (CaregiverId caregiverId, string reason, DateTime utcNow) =
            Assert.Single(gateway.DeactivationCalls);

        Assert.Equal(CaregiverIdValue, caregiverId);
        Assert.Equal(DeactivationReason, reason);
        Assert.Equal(FixedDateTimeProvider.UtcNowValue, utcNow);

        Assert.Equal(user.Id, gateway.LastRequestedUserId);

        Assert.Equal(
            new[] { "GetCaregiverAccount", "HasActiveBookings", "DeactivateCaregiver" },
            gateway.CallLog);

        Assert.True(dbContext.SaveChangesCalls > 0);
    }

    [Fact]
    public async Task DeleteMyCaregiverAccount_ReturnsCaregiverOnly_ForFamilyOnlyUser()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedFamilyOnlyUserAsync(dbContext);

        dbContext.ResetSaveChangesCalls();

        var gateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = null
        };

        var handler = CreateHandler(dbContext, gateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.CaregiverOnly, result.Error);

        // Nothing mutated, nothing else called.
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.NotNull(user.Email);
        Assert.Contains(user.Accounts, a => a.AccountType == AccountType.Family);
        Assert.Equal(new[] { "GetCaregiverAccount" }, gateway.CallLog);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteMyCaregiverAccount_DeletesCaregiverSideOnly_ForHybridUser()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedHybridUserAsync(dbContext);

        DeviceSession session = CreateSession(user.Id, FixedDateTimeProvider.UtcNowValue);
        dbContext.DeviceSessions.Add(session);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        string originalEmail = user.Email!.Value;
        string originalArabic = user.ArabicFullName.Value;
        string originalEnglish = user.EnglishFullName.Value;
        string originalPhone = user.PhoneNumber.Value;

        var gateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = new CaregiverAccountInfo(CaregiverIdValue, IsDeactivated: false)
        };

        var handler = CreateHandler(dbContext, gateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // HYBRID → the login and the Family side stay alive.
        Assert.NotEqual(UserStatus.Blocked, user.Status);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(originalEmail, user.Email?.Value);
        Assert.Equal(originalArabic, user.ArabicFullName.Value);
        Assert.Equal(originalEnglish, user.EnglishFullName.Value);
        Assert.Equal(originalPhone, user.PhoneNumber.Value);
        Assert.True(user.EmailVerified);

        // The caregiver account type is gone; the Family account type remains.
        Assert.DoesNotContain(
            user.Accounts,
            a => a.AccountType is AccountType.MedicalCaregiver
                              or AccountType.CompanionCaregiver);

        Assert.Contains(user.Accounts, a => a.AccountType == AccountType.Family);
        Assert.Single(user.Accounts);

        var deactivation = Assert.Single(gateway.DeactivationCalls);
        Assert.Equal(CaregiverIdValue, deactivation.caregiverId);

        // His family sessions must survive.
        Assert.Null(session.RevokedOnUtc);

        Assert.Equal(
            new[] { "GetCaregiverAccount", "HasActiveBookings", "DeactivateCaregiver" },
            gateway.CallLog);
    }

    [Fact]
    public async Task DeleteMyCaregiverAccount_ReturnsActiveBookingExists_AndMutatesNothing()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedCaregiverUserAsync(dbContext, AccountType.CompanionCaregiver);

        DeviceSession session = CreateSession(user.Id, FixedDateTimeProvider.UtcNowValue);
        dbContext.DeviceSessions.Add(session);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        string originalEmail = user.Email!.Value;
        string originalArabic = user.ArabicFullName.Value;

        var gateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = new CaregiverAccountInfo(CaregiverIdValue, IsDeactivated: false),
            ActiveBookingsResult = Result<bool>.Success(true)
        };

        var handler = CreateHandler(dbContext, gateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.ActiveBookingExists, result.Error);

        // D11: fail-safe, nothing was mutated and the profile is untouched.
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(originalEmail, user.Email?.Value);
        Assert.Equal(originalArabic, user.ArabicFullName.Value);
        Assert.Contains(user.Accounts, a => a.AccountType == AccountType.CompanionCaregiver);
        Assert.Null(session.RevokedOnUtc);
        Assert.Empty(gateway.DeactivationCalls);
        Assert.Equal(new[] { "GetCaregiverAccount", "HasActiveBookings" }, gateway.CallLog);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteMyCaregiverAccount_Succeeds_WhenOnlyPendingPaymentBooking()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedCaregiverUserAsync(dbContext, AccountType.MedicalCaregiver);

        dbContext.ResetSaveChangesCalls();

        // A PendingPayment booking does not commit the slot, so the D11 guard
        // reports no active booking.
        var gateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = new CaregiverAccountInfo(CaregiverIdValue, IsDeactivated: false),
            ActiveBookingsResult = Result<bool>.Success(false)
        };

        var handler = CreateHandler(dbContext, gateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(UserStatus.Blocked, user.Status);
        Assert.Null(user.Email);
        Assert.Single(gateway.DeactivationCalls);
        Assert.Equal(1, gateway.HasActiveBookingsCalls);
    }

    [Fact]
    public async Task DeleteMyCaregiverAccount_IsIdempotent_WhenPureUserAlreadyBlocked()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedCaregiverUserAsync(dbContext, AccountType.MedicalCaregiver);

        // A previous attempt already anonymized the Identity side, then failed
        // before the caregiver profile was deactivated.
        user.Block("Blocked for test setup.", FixedDateTimeProvider.UtcNowValue);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        string blockedEmail = user.Email!.Value;
        string blockedArabicName = user.ArabicFullName.Value;
        DateTime blockedUpdatedOnUtc = user.UpdatedOnUtc;

        var gateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = new CaregiverAccountInfo(CaregiverIdValue, IsDeactivated: false)
        };

        var handler = CreateHandler(dbContext, gateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Anonymize was skipped (retry converges straight to the profile).
        Assert.Equal(UserStatus.Blocked, user.Status);
        Assert.Equal(blockedEmail, user.Email?.Value);
        Assert.Equal(blockedArabicName, user.ArabicFullName.Value);
        Assert.Equal(blockedUpdatedOnUtc, user.UpdatedOnUtc);
        Assert.Equal(0, dbContext.SaveChangesCalls);

        // The still-active profile is deactivated now.
        var deactivation = Assert.Single(gateway.DeactivationCalls);
        Assert.Equal(CaregiverIdValue, deactivation.caregiverId);
        Assert.Equal(
            new[] { "GetCaregiverAccount", "HasActiveBookings", "DeactivateCaregiver" },
            gateway.CallLog);
    }

    [Fact]
    public async Task DeleteMyCaregiverAccount_CompletesPartialHybridDeletion_WhenProfileAlreadyDeactivated()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedHybridUserAsync(dbContext);

        DeviceSession session = CreateSession(user.Id, FixedDateTimeProvider.UtcNowValue);
        dbContext.DeviceSessions.Add(session);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        // The profile was deactivated, but the caregiver account types are
        // still on the user: the hybrid retry only finishes the removal.
        var gateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = new CaregiverAccountInfo(CaregiverIdValue, IsDeactivated: true)
        };

        var handler = CreateHandler(dbContext, gateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.DoesNotContain(
            user.Accounts,
            a => a.AccountType is AccountType.MedicalCaregiver
                              or AccountType.CompanionCaregiver);

        Assert.Contains(user.Accounts, a => a.AccountType == AccountType.Family);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Null(session.RevokedOnUtc);

        Assert.Empty(gateway.DeactivationCalls);
        Assert.Equal(0, gateway.HasActiveBookingsCalls);
        Assert.Equal(new[] { "GetCaregiverAccount" }, gateway.CallLog);
        Assert.Equal(1, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteMyCaregiverAccount_ReturnsCaregiverProfileNotFound_WhenAccountTypeHasNoProfile()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        // Caregiver account type, but the Caregivers module has no profile row.
        User user = await SeedCaregiverUserAsync(dbContext, AccountType.MedicalCaregiver);

        dbContext.ResetSaveChangesCalls();

        var gateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = null
        };

        var handler = CreateHandler(dbContext, gateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.CaregiverProfileNotFound, result.Error);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Contains(user.Accounts, a => a.AccountType == AccountType.MedicalCaregiver);
        Assert.Equal(new[] { "GetCaregiverAccount" }, gateway.CallLog);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    private static DeleteMyCaregiverAccountCommandHandler CreateHandler(
        IdentityTestDbContext dbContext,
        FakeCaregiverAccountGateway gateway)
    {
        return new DeleteMyCaregiverAccountCommandHandler(
            dbContext,
            gateway,
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

    private static async Task<User> SeedCaregiverUserAsync(
        IdentityTestDbContext dbContext,
        AccountType caregiverAccountType)
    {
        User user = User.Create(
            FullName.Create("كريم طبيب"),
            FullName.Create("Karim Tabib"),
            Email.Create("caregiver@example.com"),
            PhoneNumber.Create("+201001234570"),
            "avatar_key");

        user.AddAccount(caregiverAccountType);

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

    private static async Task<User> SeedFamilyOnlyUserAsync(
        IdentityTestDbContext dbContext)
    {
        User user = User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create("family@example.com"),
            PhoneNumber.Create("+201001234571"));

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
            PhoneNumber.Create("+201001234572"));

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

    private sealed class FakeCaregiverAccountGateway : ICaregiverAccountGateway
    {
        internal CaregiverAccountInfo? CaregiverAccount { get; set; }

        internal Result<bool> ActiveBookingsResult { get; set; } =
            Result<bool>.Success(false);

        internal Result DeactivationResult { get; set; } =
            Result.Success();

        internal List<string> CallLog { get; } = [];

        internal List<(CaregiverId caregiverId, string reason, DateTime utcNow)>
            DeactivationCalls { get; } = [];

        internal int HasActiveBookingsCalls { get; private set; }

        internal UserId LastRequestedUserId { get; private set; }

        public Task<CaregiverAccountInfo?> GetCaregiverAccountAsync(
            UserId userId,
            CancellationToken cancellationToken = default)
        {
            CallLog.Add("GetCaregiverAccount");
            LastRequestedUserId = userId;

            return Task.FromResult(CaregiverAccount);
        }

        public Task<Result<bool>> HasActiveBookingsAsync(
            CaregiverId caregiverId,
            CancellationToken cancellationToken = default)
        {
            CallLog.Add("HasActiveBookings");
            HasActiveBookingsCalls++;

            return Task.FromResult(ActiveBookingsResult);
        }

        public Task<Result> DeactivateCaregiverAsync(
            CaregiverId caregiverId,
            string reason,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            CallLog.Add("DeactivateCaregiver");
            DeactivationCalls.Add((caregiverId, reason, utcNow));

            return Task.FromResult(DeactivationResult);
        }
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
