using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.API;
using Sanad.API.Authorization;
using Sanad.API.CareHomesIntegration;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Users.Admin;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.Identity;

public sealed class AdminUserManagementTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ListDetailAndSessionQueries_WorkAgainstInMemoryAndSqlite(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User arabic = AddUser(contexts.Identity, AccountType.Family, "أحمد", "Zed", "ahmed@example.test", "+201000000001", UserStatus.Active);
        User english = AddUser(contexts.Identity, AccountType.SuperAdmin, "يوسف", "Adam", "admin@example.test", "+201000000002", UserStatus.Active);
        User pending = AddUser(contexts.Identity, AccountType.CareHomeOwner, "أحمد", "Adam", "owner@example.test", "+201000000003", UserStatus.PendingVerification);
        CareHomeFacility ownerFacility = CareHomeFacility.CreateDraft(pending.Id, Now);
        contexts.CareHomes.Facilities.Add(ownerFacility);
        DeviceSession active = AddSession(contexts.Identity, english.Id, Now.AddDays(-2), Now.AddDays(1), Now.AddHours(-1));
        AddSession(contexts.Identity, english.Id, Now.AddDays(-3), Now.AddDays(1), null, revoked: true);
        AddSession(contexts.Identity, arabic.Id, Now.AddDays(-3), Now.AddDays(-1), null);
        await contexts.Identity.SaveChangesAsync();
        await contexts.CareHomes.SaveChangesAsync();

        var listHandler = new AdminUserListQueryHandler(contexts.Identity);
        AdminUserListResponse namePage = (await listHandler.Handle(new(null, null, null, "Name", 1, 100), default)).Value;
        Assert.Equal(new[] { pending.Id.Value, arabic.Id.Value, english.Id.Value }, namePage.Items.Select(x => x.Id));
        Assert.True(namePage.Items.Single(x => x.Id == arabic.Id.Value).IsVerified);
        Assert.False(namePage.Items.Single(x => x.Id == pending.Id.Value).IsVerified);
        Assert.Equal(9, Enum.GetValues<AccountType>().Length);

        AdminUserListResponse searched = (await listHandler.Handle(new("AHMED", null, null, "Newest", 1, 20), default)).Value;
        Assert.Single(searched.Items);
        AdminUserListResponse arabicSearch = (await listHandler.Handle(new("أحمد", null, null, "Newest", 1, 20), default)).Value;
        Assert.Equal(2, arabicSearch.TotalCount);
        AdminUserListResponse filtered = (await listHandler.Handle(new(null, "SuperAdmin", "Active", "Oldest", 1, 20), default)).Value;
        Assert.Equal(new[] { english.Id.Value }, filtered.Items.Select(x => x.Id));

        var facilityLookup = new ApiCareHomeOwnerFacilityLookup(contexts.CareHomes);
        var detailHandler = new AdminUserDetailQueryHandler(contexts.Identity, facilityLookup, new FixedClock(Now));
        AdminUserDetailResponse detail = (await detailHandler.Handle(new(english.Id), default)).Value;
        Assert.Equal(new[] { active.Id.Value }, detail.Sessions.Select(x => x.DeviceId));
        Assert.Equal(Now.AddHours(-1), detail.Sessions[0].LastSeenUtc);
        Assert.Null(detail.Linked.FacilityId);
        AdminUserDetailResponse ownerDetail = (await detailHandler.Handle(new(pending.Id), default)).Value;
        Assert.False(ownerDetail.IsVerified);
        Assert.Equal(ownerFacility.Id.Value, ownerDetail.Linked.FacilityId);
        Assert.Equal(CareHomeStatus.Draft.ToString(), ownerDetail.Linked.FacilityStatus);
        Assert.Equal(AdminUserErrors.NotFound.Code, (await detailHandler.Handle(new(UserId.New()), default)).Error.Code);

        var overviewHandler = new AdminSessionOverviewQueryHandler(contexts.Identity, new FixedClock(Now));
        AdminSessionOverviewResponse overview = (await overviewHandler.Handle(new("SuperAdmin", null, "Newest", 1, 20), default)).Value;
        Assert.Single(overview.Items);
        Assert.Equal(active.Id.Value, overview.Items[0].Session.DeviceId);
        AdminSessionOverviewResponse nameOrderedOverview = (await overviewHandler.Handle(new(null, null, "Name", 1, 20), default)).Value;
        Assert.Equal(active.Id.Value, nameOrderedOverview.Items[0].Session.DeviceId);
        Assert.Equal(new[] { AccountType.SuperAdmin }, nameOrderedOverview.Items[0].AccountTypes);

        Assert.True(AdminQueryParsing.TryPage(0, 101, out int normalizedPage, out int normalizedSize));
        Assert.Equal(1, normalizedPage);
        Assert.Equal(100, normalizedSize);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FacilityLookup_TranslatesAndReturnsOwnerFacility(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User owner = AddUser(contexts.Identity, AccountType.CareHomeOwner, "مالك", "Owner", "owner@example.test", "+201000000010", UserStatus.Active);
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner.Id, Now);
        contexts.CareHomes.Facilities.Add(facility);
        await contexts.CareHomes.SaveChangesAsync();

        CareHomeOwnerFacility? result = await new ApiCareHomeOwnerFacilityLookup(contexts.CareHomes).FindAsync(owner.Id, default);
        Assert.NotNull(result);
        Assert.Equal(facility.Id.Value, result.FacilityId);
        Assert.Equal(CareHomeStatus.Draft.ToString(), result.FacilityStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Suspend_RevokesSessionsAndPublishesNotice(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.SuperAdmin, "مدير", "Admin", "admin@example.test", "+201000000011", UserStatus.Active);
        User target = AddUser(contexts.Identity, AccountType.Family, "مستخدم", "User", "user@example.test", "+201000000012", UserStatus.Active);
        DeviceSession session = AddSession(contexts.Identity, target.Id, Now, Now.AddDays(1), null);
        await contexts.Identity.SaveChangesAsync();
        var notices = new RecordingNoticePublisher();

        var handler = new SuspendAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), notices);
        Result<AdminUserStatusResponse> result = await handler.Handle(new(actor.Id, target.Id, "Security review"), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Suspended, target.Status);
        Assert.Equal(1, result.Value.RevokedSessionCount);
        Assert.Equal(Now, session.RevokedOnUtc);
        Assert.Single(notices.Items);
        Assert.Equal(UserStatus.Suspended, notices.Items[0].Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StatusCommands_EnforceSelfSeededAndLastSuperAdminGuards(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.Family, "مدير", "Admin", "admin@example.test", "+201000000013", UserStatus.Active);
        User operatorUser = AddUser(contexts.Identity, AccountType.SuperAdmin, "مشغل", "Operator", "operator@example.test", "+201000000014", UserStatus.Active);
        User seeded = AddUser(contexts.Identity, AccountType.Family, "أساسي", "Seeded", "seed@example.test", "+201000000015", UserStatus.Active);
        await contexts.Identity.SaveChangesAsync();
        var notices = new RecordingNoticePublisher();
        var block = new BlockAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new EmailSeedProtection("seed@example.test"), notices);

        Assert.Equal(AdminUserErrors.SelfAction.Code, (await block.Handle(new(operatorUser.Id, operatorUser.Id, "self"), default)).Error.Code);
        Assert.Equal(AdminUserErrors.SeededProtected.Code, (await block.Handle(new(actor.Id, seeded.Id, "seed"), default)).Error.Code);
        Assert.Equal(AdminUserErrors.LastSuperAdmin.Code, (await block.Handle(new(actor.Id, operatorUser.Id, "last"), default)).Error.Code);
        Assert.Equal(AdminUserErrors.SeededProtected.Code, (await new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new EmailSeedProtection("seed@example.test"), notices).Handle(new(actor.Id, seeded.Id), default)).Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReactivateAndRevokeSessionCommands_UseFrozenContract(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.SuperAdmin, "مدير", "Admin", "admin2@example.test", "+201000000016", UserStatus.Active);
        User target = AddUser(contexts.Identity, AccountType.Family, "مستخدم", "User", "user2@example.test", "+201000000017", UserStatus.Active);
        target.Suspend("Temporary", Now);
        DeviceSession session = AddSession(contexts.Identity, target.Id, Now, Now.AddDays(1), null);
        await contexts.Identity.SaveChangesAsync();
        var notices = new RecordingNoticePublisher();

        Result<AdminUserStatusResponse> reactivated = await new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(1)), new NeverSeededProtection(), notices).Handle(new(actor.Id, target.Id), default);
        Assert.Equal(UserStatus.Active, reactivated.Value.Status);
        Assert.Contains(notices.Items, notice => notice.Status == UserStatus.Active);
        Assert.Equal(AdminUserErrors.NotSuspended.Code, (await new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(1)), new NeverSeededProtection(), notices).Handle(new(actor.Id, target.Id), default)).Error.Code);

        var revokeHandler = new RevokeAdminUserSessionsCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(2)));
        Result invalidSpecificSession = await revokeHandler.Handle(new(target.Id, new DeviceSessionId(Guid.Empty)), default);
        Assert.Equal(AdminUserErrors.NotFound.Code, invalidSpecificSession.Error.Code);
        Assert.Null(session.RevokedOnUtc);

        Result revoke = await revokeHandler.Handle(new(target.Id, session.Id), default);
        Assert.True(revoke.IsSuccess);
        Assert.NotNull(session.RevokedOnUtc);

        DeviceSession secondSession = AddSession(contexts.Identity, target.Id, Now, Now.AddDays(1), null);
        await contexts.Identity.SaveChangesAsync();
        Result revokeAll = await new RevokeAdminUserSessionsCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(3))).Handle(new(target.Id, null), default);
        Assert.True(revokeAll.IsSuccess);
        Assert.NotNull(secondSession.RevokedOnUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Block_RevokesSessionsAndPublishesNotice_AndRepeatedTransitionsConflict(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.SuperAdmin, "مدير", "Admin", "admin3@example.test", "+201000000018", UserStatus.Active);
        User target = AddUser(contexts.Identity, AccountType.Family, "مستخدم", "User", "user3@example.test", "+201000000019", UserStatus.Active);
        DeviceSession session = AddSession(contexts.Identity, target.Id, Now, Now.AddDays(1), null);
        await contexts.Identity.SaveChangesAsync();
        var notices = new RecordingNoticePublisher();
        var handler = new BlockAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), notices);

        Result<AdminUserStatusResponse> result = await handler.Handle(new(actor.Id, target.Id, "Security incident"), default);
        Assert.True(result.IsSuccess);
        Assert.Equal(UserStatus.Blocked, result.Value.Status);
        Assert.Equal(1, result.Value.RevokedSessionCount);
        Assert.NotNull(session.RevokedOnUtc);
        Assert.Contains(notices.Items, notice => notice.Status == UserStatus.Blocked);
        Assert.Equal(AdminUserErrors.AlreadyBlocked.Code, (await handler.Handle(new(actor.Id, target.Id, "Repeated"), default)).Error.Code);

        User suspended = AddUser(contexts.Identity, AccountType.Family, "موقوف", "Suspended", "suspended@example.test", "+201000000020", UserStatus.Active);
        suspended.Suspend("Existing suspension", Now);
        await contexts.Identity.SaveChangesAsync();
        Assert.Equal(AdminUserErrors.AlreadySuspended.Code, (await new SuspendAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), notices).Handle(new(actor.Id, suspended.Id, "Repeated"), default)).Error.Code);
    }

    [Fact]
    public async Task SuperAdminOnly_RequiresNormalAccessAndSuperAdminAccount()
    {
        var services = new ServiceCollection();
        services.AddSanadApi(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:IdentityDatabase"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["Identity:Jwt:Issuer"] = "Sanad.Api",
            ["Identity:Jwt:Audience"] = "Sanad.Clients",
            ["Identity:Jwt:SigningKey"] = "12345678901234567890123456789012"
        }).Build());
        using ServiceProvider provider = services.BuildServiceProvider();
        AuthorizationPolicy policy = (await provider.GetRequiredService<IAuthorizationPolicyProvider>().GetPolicyAsync(AuthorizationPolicies.SuperAdminOnly))!;
        Assert.Contains(policy.Requirements, x => x is DenyAnonymousAuthorizationRequirement);
        Assert.Equal(new[] { AuthAccessType.Normal.ToString() }, policy.Requirements.OfType<ClaimsAuthorizationRequirement>().Single(x => x.ClaimType == AuthClaimNames.AccessType).AllowedValues);
        Assert.Equal(new[] { AccountType.SuperAdmin.ToString() }, policy.Requirements.OfType<ClaimsAuthorizationRequirement>().Single(x => x.ClaimType == AuthClaimNames.AccountType).AllowedValues);
    }

    private static User AddUser(IdentityDbContext db, AccountType type, string arabic, string english, string email, string phone, UserStatus status)
    {
        User user = User.Create(FullName.Create(arabic), FullName.Create(english), Email.Create(email), PhoneNumber.Create(phone));
        user.AddAccount(type);
        if (status == UserStatus.Active)
        {
            user.SetInitialPasswordHash("test-hash", Now);
            user.VerifyEmail(Now);
            user.VerifyPhone(Now);
            user.Activate(Now);
        }
        db.Users.Add(user);
        return user;
    }

    private static DeviceSession AddSession(IdentityDbContext db, UserId userId, DateTime created, DateTime expires, DateTime? lastRotated, bool revoked = false)
    {
        DeviceSession session = DeviceSession.Create(userId, "Test device", DevicePlatform.Android, "1.0", Guid.NewGuid().ToString("N"), created, expires);
        if (lastRotated is not null)
            session.RotateRefreshToken(Guid.NewGuid().ToString("N"), expires, lastRotated.Value);
        if (revoked)
            session.Revoke("test", Now);
        db.DeviceSessions.Add(session);
        return session;
    }

    private sealed class NeverSeededProtection : IAdminUserSeedProtection
    {
        public bool IsSeeded(User user) => false;
    }

    private sealed class EmailSeedProtection(string email) : IAdminUserSeedProtection
    {
        public bool IsSeeded(User user) => string.Equals(user.Email?.Value, email, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingNoticePublisher : IAdminUserStatusNoticePublisher
    {
        public List<AdminUserStatusNotice> Items { get; } = [];
        public Task PublishAsync(AdminUserStatusNotice notice, CancellationToken cancellationToken)
        {
            Items.Add(notice);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class TestContexts : IAsyncDisposable
    {
        private readonly SqliteConnection? _identityConnection;
        private readonly SqliteConnection? _careHomesConnection;
        private TestContexts(IdentityDbContext identity, CareHomesDbContext careHomes, SqliteConnection? identityConnection, SqliteConnection? careHomesConnection)
        {
            Identity = identity;
            CareHomes = careHomes;
            _identityConnection = identityConnection;
            _careHomesConnection = careHomesConnection;
        }
        public IdentityDbContext Identity { get; }
        public CareHomesDbContext CareHomes { get; }
        public static async Task<TestContexts> CreateAsync(bool sqlite)
        {
            SqliteConnection? identityConnection = null;
            SqliteConnection? careHomesConnection = null;
            DbContextOptions<IdentityDbContext> identityOptions;
            DbContextOptions<CareHomesDbContext> careHomesOptions;
            if (sqlite)
            {
                identityConnection = new SqliteConnection("Data Source=:memory:");
                careHomesConnection = new SqliteConnection("Data Source=:memory:");
                await identityConnection.OpenAsync();
                await careHomesConnection.OpenAsync();
                identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(identityConnection).Options;
                careHomesOptions = new DbContextOptionsBuilder<CareHomesDbContext>().UseSqlite(careHomesConnection).Options;
            }
            else
            {
                string name = Guid.NewGuid().ToString("N");
                identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(name + "-identity").Options;
                careHomesOptions = new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(name + "-carehomes").Options;
            }
            var identity = new IdentityDbContext(identityOptions);
            var careHomes = new CareHomesDbContext(careHomesOptions);
            if (sqlite)
            {
                await identity.Database.EnsureCreatedAsync();
                await careHomes.Database.EnsureCreatedAsync();
            }
            return new TestContexts(identity, careHomes, identityConnection, careHomesConnection);
        }
        public async ValueTask DisposeAsync()
        {
            await Identity.DisposeAsync();
            await CareHomes.DisposeAsync();
            if (_identityConnection is not null) await _identityConnection.DisposeAsync();
            if (_careHomesConnection is not null) await _careHomesConnection.DisposeAsync();
        }
    }
}
