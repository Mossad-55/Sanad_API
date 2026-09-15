using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Caregivers;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Account;

public sealed class AccountChoicesTests
{
    [Theory]
    [InlineData(AccountType.MedicalCaregiver, AccountType.CompanionCaregiver)]
    [InlineData(AccountType.CompanionCaregiver, AccountType.MedicalCaregiver)]
    public void Domain_RejectsBothCaregiverTypes(AccountType first, AccountType second)
    {
        var user = CreateUser(first);
        var error = Assert.Throws<DomainException>(() => user.AddAccount(second));
        Assert.Equal(UserErrors.CaregiverTypesExclusive, error.Message);
        Assert.Single(user.Accounts);
    }

    [Theory]
    [InlineData(AccountType.Family, AccountType.MedicalCaregiver)]
    [InlineData(AccountType.Family, AccountType.CompanionCaregiver)]
    [InlineData(AccountType.MedicalCaregiver, AccountType.Family)]
    [InlineData(AccountType.CompanionCaregiver, AccountType.Family)]
    public async Task Add_PersistsAllowedHybridsAndRequiresRefresh(AccountType first, AccountType second)
    {
        await using var db = CreateDb();
        var user = await Seed(db, first);
        var result = await new AddMyAccountCommandHandler(db, new Gateway()).Handle(new(user.Id, second), default);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RefreshRequired);
        Assert.Equal(second, result.Value.AccountType);
        Assert.Equal(1, db.SaveChangesCalls);
        db.ChangeTracker.Clear();
        var saved = await db.Users.SingleAsync();
        Assert.Equal(2, saved.Accounts.Count);
        Assert.Empty(await db.DeviceSessions.ToListAsync());
    }

    [Theory]
    [InlineData(AccountType.MedicalCaregiver, AccountType.CompanionCaregiver)]
    [InlineData(AccountType.CompanionCaregiver, AccountType.MedicalCaregiver)]
    public async Task Add_RejectsOppositeCaregiverWithoutWriting(AccountType first, AccountType second)
    {
        await using var db = CreateDb();
        var user = await Seed(db, first);
        var result = await new AddMyAccountCommandHandler(db, new Gateway()).Handle(new(user.Id, second), default);
        Assert.Equal(AccountErrors.CaregiverTypesExclusive, result.Error);
        Assert.Equal(0, db.SaveChangesCalls);
        Assert.Single(user.Accounts);
    }

    [Theory]
    [InlineData(AccountType.Elderly)]
    [InlineData(AccountType.SuperAdmin)]
    [InlineData(AccountType.ContentAdmin)]
    [InlineData(AccountType.SupportAdmin)]
    [InlineData((AccountType)999)]
    public async Task AddAndSwitch_RejectUnsupportedTarget(AccountType target)
    {
        await using var db = CreateDb();
        var user = await Seed(db, AccountType.Family);
        var add = await new AddMyAccountCommandHandler(db, new Gateway()).Handle(new(user.Id, target), default);
        var change = await new SwitchMyAccountCommandHandler(db).Handle(new(user.Id, target), default);
        Assert.Equal(AccountErrors.UnsupportedAccountType, add.Error);
        Assert.Equal(AccountErrors.UnsupportedAccountType, change.Error);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task Add_RejectsDuplicate()
    {
        await using var db = CreateDb();
        var user = await Seed(db, AccountType.Family);
        var result = await new AddMyAccountCommandHandler(db, new Gateway()).Handle(new(user.Id, AccountType.Family), default);
        Assert.Equal(AccountErrors.AccountAlreadyExists, result.Error);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Add_DoesNotReactivateOrOverwriteRetainedCaregiver(bool deactivated)
    {
        await using var db = CreateDb();
        var user = await Seed(db, AccountType.Family);
        var gateway = new Gateway { Info = new(new CaregiverId(Guid.NewGuid()), deactivated) };
        var result = await new AddMyAccountCommandHandler(db, gateway).Handle(new(user.Id, AccountType.MedicalCaregiver), default);
        Assert.Equal(AccountErrors.RetainedCaregiverProfile, result.Error);
        Assert.Equal(0, db.SaveChangesCalls);
        Assert.Single(user.Accounts);
    }

    [Fact]
    public async Task Switch_IsClientLocalAndCannotSelectUnownedAccount()
    {
        await using var db = CreateDb();
        var user = await Seed(db, AccountType.Family);
        user.AddAccount(AccountType.MedicalCaregiver);
        await db.SaveChangesAsync();
        db.ResetSaveChangesCalls();
        var before = user.UpdatedOnUtc;
        var handler = new SwitchMyAccountCommandHandler(db);
        var selected = await handler.Handle(new(user.Id, AccountType.MedicalCaregiver), default);
        var missing = await handler.Handle(new(user.Id, AccountType.CompanionCaregiver), default);
        Assert.True(selected.IsSuccess);
        Assert.Equal(AccountType.MedicalCaregiver, selected.Value.AccountType);
        Assert.Equal(AccountErrors.AccountNotOwned, missing.Error);
        Assert.Equal(0, db.SaveChangesCalls);
        Assert.Equal(before, user.UpdatedOnUtc);
        Assert.Equal(2, user.Accounts.Count);
        Assert.Empty(await db.DeviceSessions.ToListAsync());
    }

    [Fact]
    public async Task List_IsOrderedAndReadOnly()
    {
        await using var db = CreateDb();
        var user = await Seed(db, AccountType.CompanionCaregiver);
        user.AddAccount(AccountType.Family);
        await db.SaveChangesAsync();
        db.ResetSaveChangesCalls();
        var result = await new GetMyAccountsQueryHandler(db).Handle(new(user.Id), default);
        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { AccountType.Family, AccountType.CompanionCaregiver }, result.Value.Accounts);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task MissingUser_ReturnsNotFound()
    {
        await using var db = CreateDb();
        var id = new UserId(Guid.NewGuid());
        Assert.Equal(AccountErrors.UserNotFound, (await new GetMyAccountsQueryHandler(db).Handle(new(id), default)).Error);
        Assert.Equal(AccountErrors.UserNotFound, (await new AddMyAccountCommandHandler(db, new Gateway()).Handle(new(id, AccountType.Family), default)).Error);
        Assert.Equal(AccountErrors.UserNotFound, (await new SwitchMyAccountCommandHandler(db).Handle(new(id, AccountType.Family), default)).Error);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Theory]
    [InlineData(UserStatus.PendingVerification)]
    [InlineData(UserStatus.Suspended)]
    [InlineData(UserStatus.Blocked)]
    public async Task InactiveUsers_CannotManageChoices(UserStatus status)
    {
        await using var db = CreateDb();
        var user = CreateUser(AccountType.Family, status);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ResetSaveChangesCalls();
        Assert.Equal(AccountErrors.InvalidOperation, (await new GetMyAccountsQueryHandler(db).Handle(new(user.Id), default)).Error);
        Assert.Equal(AccountErrors.InvalidOperation, (await new AddMyAccountCommandHandler(db, new Gateway()).Handle(new(user.Id, AccountType.MedicalCaregiver), default)).Error);
        Assert.Equal(AccountErrors.InvalidOperation, (await new SwitchMyAccountCommandHandler(db).Handle(new(user.Id, AccountType.Family), default)).Error);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Theory]
    [InlineData(AccountType.SuperAdmin)]
    [InlineData(AccountType.ContentAdmin)]
    [InlineData(AccountType.SupportAdmin)]
    public async Task AdminIdentity_CannotAddRegularAccount(AccountType adminType)
    {
        await using var db = CreateDb();
        var user = await Seed(db, adminType);
        Assert.Equal(AccountErrors.InvalidOperation, (await new AddMyAccountCommandHandler(db, new Gateway()).Handle(new(user.Id, AccountType.Family), default)).Error);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public void Validators_RejectEmptyIdentityAndUndefinedTypes()
    {
        Assert.False(new GetMyAccountsQueryValidator().Validate(new(UserId.Empty)).IsValid);
        Assert.False(new AddMyAccountCommandValidator().Validate(new(UserId.Empty, AccountType.Family)).IsValid);
        Assert.False(new SwitchMyAccountCommandValidator().Validate(new(UserId.Empty, AccountType.Family)).IsValid);
        var id = new UserId(Guid.NewGuid());
        Assert.False(new AddMyAccountCommandValidator().Validate(new(id, (AccountType)999)).IsValid);
        Assert.False(new SwitchMyAccountCommandValidator().Validate(new(id, (AccountType)999)).IsValid);
    }

    [Fact]
    public async Task ElderlyIdentity_CannotAddOrSwitchRegularAccounts()
    {
        await using var db = CreateDb();
        var user = User.CreateElderly(FullName.Create("محمد أحمد"), FullName.Create("Mohamed Ahmed"),
            PhoneNumber.Create("+201001234567"), Gender.Male, new DateOnly(1950, 1, 1), DateTime.UtcNow);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ResetSaveChangesCalls();
        Assert.Equal(AccountErrors.InvalidOperation, (await new AddMyAccountCommandHandler(db, new Gateway()).Handle(new(user.Id, AccountType.Family), default)).Error);
        Assert.Equal(AccountErrors.InvalidOperation, (await new SwitchMyAccountCommandHandler(db).Handle(new(user.Id, AccountType.Family), default)).Error);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KnownConcurrentWrite_IsReturnedAsConflict(bool exclusivity)
    {
        await using var db = CreateDb();
        var user = await Seed(db, AccountType.Family);
        var failing = new FailingContext(db, new AccountWriteConflictException(exclusivity, new Exception("simulated constraint")));
        var result = await new AddMyAccountCommandHandler(failing, new Gateway()).Handle(new(user.Id, AccountType.MedicalCaregiver), default);
        Assert.Equal(exclusivity ? AccountErrors.CaregiverTypesExclusive : AccountErrors.AccountAlreadyExists, result.Error);
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task UnrelatedPersistenceError_IsNotMisreportedAsAccountConflict()
    {
        await using var db = CreateDb();
        var user = await Seed(db, AccountType.Family);
        var failing = new FailingContext(db, new DbUpdateException("simulated outage"));
        await Assert.ThrowsAsync<DbUpdateException>(() => new AddMyAccountCommandHandler(failing, new Gateway())
            .Handle(new(user.Id, AccountType.MedicalCaregiver), default));
    }

    [Theory]
    [InlineData(nameof(AccountController.GetAccounts))]
    [InlineData(nameof(AccountController.AddAccount))]
    [InlineData(nameof(AccountController.SwitchAccount))]
    public void Endpoints_RequireNormalAccess(string methodName)
    {
        var method = typeof(AccountController).GetMethod(methodName)!;
        var auth = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.NormalAccess, auth.Policy);
        Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Theory]
    [InlineData("Identity.Account.UnsupportedAccountType", 403)]
    [InlineData("Identity.Account.AccountNotOwned", 403)]
    [InlineData("Identity.Account.AccountAlreadyExists", 409)]
    [InlineData("Identity.Account.CaregiverTypesExclusive", 409)]
    [InlineData("Identity.Account.RetainedCaregiverProfile", 409)]
    public void Errors_HaveExplicitHttpMappings(string code, int status)
    {
        var problem = ResultProblemDetailsMapper.Create(new Error(code, "test"), new DefaultHttpContext());
        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
    }

    private sealed class FailingContext(IdentityTestDbContext inner, Exception failure) : IIdentityDbContext
    {
        public DbSet<User> Users => inner.Users;
        public DbSet<VerificationRequest> VerificationRequests => inner.VerificationRequests;
        public DbSet<DeviceSession> DeviceSessions => inner.DeviceSessions;
        public DbSet<SupportTicket> SupportTickets => inner.SupportTickets;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromException<int>(failure);
    }

    private static IdentityTestDbContext CreateDb() => new(new DbContextOptionsBuilder<IdentityTestDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<User> Seed(IdentityTestDbContext db, AccountType type)
    {
        var user = CreateUser(type);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ResetSaveChangesCalls();
        return user;
    }

    private static User CreateUser(AccountType type, UserStatus status = UserStatus.Active)
    {
        var user = User.Create(FullName.Create("محمد أحمد"), FullName.Create("Mohamed Ahmed"),
            Email.Create("account-choice@example.com"), PhoneNumber.Create("+201001234567"));
        user.AddAccount(type);
        var now = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        user.SetInitialPasswordHash("test-hash", now);
        if (status != UserStatus.PendingVerification)
        {
            user.VerifyEmail(now);
            user.VerifyPhone(now);
            user.Activate(now);
        }
        if (status == UserStatus.Suspended) user.Suspend("test", now);
        if (status == UserStatus.Blocked) user.Block("test", now);
        return user;
    }

    private sealed class Gateway : ICaregiverAccountGateway
    {
        public CaregiverAccountInfo? Info { get; init; }
        public Task<CaregiverAccountInfo?> GetCaregiverAccountAsync(UserId userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Info);
        public Task<Result<bool>> HasActiveBookingsAsync(CaregiverId caregiverId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Account selection must not query bookings.");
        public Task<Result> DeactivateCaregiverAsync(CaregiverId caregiverId, string reason, DateTime utcNow, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Account selection must not deactivate profiles.");
    }
}
