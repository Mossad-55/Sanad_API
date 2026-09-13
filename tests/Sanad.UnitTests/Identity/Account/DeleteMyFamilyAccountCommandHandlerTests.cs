using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Caregivers;
using Sanad.Modules.Identity.Application.Abstractions.Families;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Account;

public sealed class DeleteMyFamilyAccountCommandHandlerTests
{
    [Fact]
    public async Task DeleteMyAccount_FamilyOnly_Success_LeavesAnonymizesRevokesSessions()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedFamilyOnlyUserAsync(dbContext);

        DeviceSession session = CreateSession(user.Id, FixedDateTimeProvider.UtcNowValue);
        dbContext.DeviceSessions.Add(session);
        await dbContext.SaveChangesAsync();
        dbContext.ResetSaveChangesCalls();

        var caregiverGateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = null
        };

        var familyGateway = new FakeFamilyAccountGateway
        {
            IsElderlyDependent = false,
            ActiveRoles = new List<FamilyMembership>
            {
                new(new FamilyId(Guid.NewGuid()), FamilyRole.Editor),
                new(new FamilyId(Guid.NewGuid()), FamilyRole.Viewer)
            }
        };

        var handler = CreateHandler(dbContext, caregiverGateway, familyGateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Should have called family checks and leave
        Assert.Equal(1, familyGateway.IsElderlyCalls);
        Assert.Equal(1, familyGateway.GetActiveRolesCalls);
        Assert.Equal(1, familyGateway.LeaveCalls);

        // Anonymize & retain
        Assert.Equal(UserStatus.Blocked, user.Status);
        Assert.Equal("محذوف", user.ArabicFullName.Value);
        Assert.Equal("Deleted", user.EnglishFullName.Value);
        Assert.Null(user.Email);
        Assert.False(user.EmailVerified);
        Assert.False(user.PhoneVerified);
        Assert.StartsWith("+200", user.PhoneNumber.Value);
        Assert.Null(user.AvatarUrl);

        // Sessions revoked
        Assert.NotNull(session.RevokedOnUtc);
        Assert.Equal("The account was deleted by its owner.", session.RevocationReason);

        Assert.True(dbContext.SaveChangesCalls > 0);
    }

    [Fact]
    public async Task DeleteMyAccount_FamilyOnly_Blocked_WhenOwner()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedFamilyOnlyUserAsync(dbContext);
        dbContext.ResetSaveChangesCalls();

        var caregiverGateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = null
        };

        var familyGateway = new FakeFamilyAccountGateway
        {
            IsElderlyDependent = false,
            ActiveRoles = new List<FamilyMembership>
            {
                new(new FamilyId(Guid.NewGuid()), FamilyRole.Owner)
            }
        };

        var handler = CreateHandler(dbContext, caregiverGateway, familyGateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.OwnershipTransferRequired, result.Error);

        // Nothing mutated
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.NotNull(user.Email);
        Assert.Equal(0, familyGateway.LeaveCalls);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteMyAccount_FamilyOnly_Blocked_WhenElderlyDependent()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedFamilyOnlyUserAsync(dbContext);
        dbContext.ResetSaveChangesCalls();

        var caregiverGateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = null
        };

        var familyGateway = new FakeFamilyAccountGateway
        {
            IsElderlyDependent = true,
            ActiveRoles = new List<FamilyMembership>()
        };

        var handler = CreateHandler(dbContext, caregiverGateway, familyGateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.ElderlyManagedByFamily, result.Error);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(0, familyGateway.GetActiveRolesCalls);
        Assert.Equal(0, familyGateway.LeaveCalls);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    [Fact]
    public async Task DeleteMyAccount_FamilyOnly_Blocked_WhenLeaveFailsAsOwner()
    {
        await using IdentityTestDbContext dbContext = CreateDbContext();

        User user = await SeedFamilyOnlyUserAsync(dbContext);
        dbContext.ResetSaveChangesCalls();

        var caregiverGateway = new FakeCaregiverAccountGateway
        {
            CaregiverAccount = null
        };

        var familyGateway = new FakeFamilyAccountGateway
        {
            IsElderlyDependent = false,
            ActiveRoles = new List<FamilyMembership>
            {
                new(new FamilyId(Guid.NewGuid()), FamilyRole.Editor)
            },
            LeaveResult = Result.Failure(new Error("Families.Family.OwnerProtected", "Owner protected"))
        };

        var handler = CreateHandler(dbContext, caregiverGateway, familyGateway);

        var command = new DeleteMyCaregiverAccountCommand(user.Id);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.OwnershipTransferRequired, result.Error);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(1, familyGateway.LeaveCalls);
        Assert.Equal(0, dbContext.SaveChangesCalls);
    }

    private static DeleteMyCaregiverAccountCommandHandler CreateHandler(
        IdentityTestDbContext dbContext,
        FakeCaregiverAccountGateway caregiverGateway,
        FakeFamilyAccountGateway familyGateway)
    {
        return new DeleteMyCaregiverAccountCommandHandler(
            dbContext,
            caregiverGateway,
            familyGateway,
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

        public Task<CaregiverAccountInfo?> GetCaregiverAccountAsync(
            UserId userId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(CaregiverAccount);
        }

        public Task<Result<bool>> HasActiveBookingsAsync(
            CaregiverId caregiverId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ActiveBookingsResult);
        }

        public Task<Result> DeactivateCaregiverAsync(
            CaregiverId caregiverId,
            string reason,
            DateTime utcNow,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DeactivationResult);
        }
    }

    private sealed class FakeFamilyAccountGateway : IFamilyAccountGateway
    {
        internal bool IsElderlyDependent { get; set; }

        internal IReadOnlyList<FamilyMembership> ActiveRoles { get; set; } = [];

        internal Result LeaveResult { get; set; } = Result.Success();

        internal int IsElderlyCalls { get; private set; }
        internal int GetActiveRolesCalls { get; private set; }
        internal int LeaveCalls { get; private set; }

        public Task<bool> IsElderlyDependentAsync(
            UserId userId,
            CancellationToken cancellationToken = default)
        {
            IsElderlyCalls++;
            return Task.FromResult(IsElderlyDependent);
        }

        public Task<IReadOnlyList<FamilyMembership>> GetActiveFamilyRolesAsync(
            UserId userId,
            CancellationToken cancellationToken = default)
        {
            GetActiveRolesCalls++;
            return Task.FromResult(ActiveRoles);
        }

        public Task<Result> LeaveFamiliesForSelfDeletionAsync(
            UserId userId,
            CancellationToken cancellationToken = default)
        {
            LeaveCalls++;
            return Task.FromResult(LeaveResult);
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
