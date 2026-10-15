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
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using FeedbackEntity = Sanad.Modules.Identity.Domain.Feedback;

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
        Result invalidSpecificSession = await revokeHandler.Handle(new(actor.Id, target.Id, new DeviceSessionId(Guid.Empty)), default);
        Assert.Equal(AdminUserErrors.NotFound.Code, invalidSpecificSession.Error.Code);
        Assert.Null(session.RevokedOnUtc);

        Result revoke = await revokeHandler.Handle(new(actor.Id, target.Id, session.Id), default);
        Assert.True(revoke.IsSuccess);
        Assert.NotNull(session.RevokedOnUtc);

        DeviceSession secondSession = AddSession(contexts.Identity, target.Id, Now, Now.AddDays(1), null);
        await contexts.Identity.SaveChangesAsync();
        Result revokeAll = await new RevokeAdminUserSessionsCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(3))).Handle(new(actor.Id, target.Id, null), default);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Reactivate_UnreadyPendingAdmin_ReturnsActivationNotReady(bool sqlite, bool block)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.SuperAdmin, "مدير", "Admin", "react.admin@example.test", "+201000001001", UserStatus.Active);
        await contexts.Identity.SaveChangesAsync();
        var mail = new RecordingVerificationSender();
        var sms = new RecordingVerificationSender();
        Result<RegisterUserResponse> created = await new CreateAdminUserCommandHandler(contexts.Identity, new TestHasher(), new TestOtpService(), mail, sms, new FixedClock(Now))
            .Handle(new(actor.Id, "مدير جديد", "New Admin", "pending.admin@example.test", "+201000001002", "Strong-Password-123", AccountType.SupportAdmin), default);
        Assert.True(created.IsSuccess);
        User target = await contexts.Identity.Users.Include(x => x.Accounts).SingleAsync(x => x.Id == created.Value.UserId);
        Assert.Equal(UserStatus.PendingVerification, target.Status);
        Assert.False(target.EmailVerified);
        Assert.False(target.PhoneVerified);
        DeviceSession session = AddSession(contexts.Identity, target.Id, Now, Now.AddDays(1), null);
        await contexts.Identity.SaveChangesAsync();
        var notices = new RecordingNoticePublisher();
        if (block)
        {
            Result<AdminUserStatusResponse> blocked = await new BlockAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), notices).Handle(new(actor.Id, target.Id, "Fraud review"), default);
            Assert.True(blocked.IsSuccess);
            Assert.Equal(UserStatus.Blocked, target.Status);
        }
        else
        {
            Result<AdminUserStatusResponse> suspended = await new SuspendAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), notices).Handle(new(actor.Id, target.Id, "Security review"), default);
            Assert.True(suspended.IsSuccess);
            Assert.Equal(UserStatus.Suspended, target.Status);
        }
        Assert.NotNull(session.RevokedOnUtc);
        UserStatus sourceStatus = target.Status;
        string? sourceReason = target.StatusReason;
        DateTime sourceUpdated = target.UpdatedOnUtc;
        int actionsBefore = await contexts.Identity.AdminUserActions.CountAsync(x => x.TargetUserId == target.Id);
        int noticesBefore = notices.Items.Count;

        Result<AdminUserStatusResponse> result = await new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(1)), new NeverSeededProtection(), notices).Handle(new(actor.Id, target.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.ActivationNotReady.Code, result.Error.Code);
        User reloaded = await contexts.Identity.Users.Include(x => x.Accounts).SingleAsync(x => x.Id == target.Id);
        Assert.Equal(sourceStatus, reloaded.Status);
        Assert.Equal(sourceReason, reloaded.StatusReason);
        Assert.Equal(sourceUpdated, reloaded.UpdatedOnUtc);
        Assert.False(reloaded.EmailVerified);
        Assert.False(reloaded.PhoneVerified);
        Assert.Equal(actionsBefore, await contexts.Identity.AdminUserActions.CountAsync(x => x.TargetUserId == target.Id));
        Assert.Equal(noticesBefore, notices.Items.Count);
        Assert.NotNull(session.RevokedOnUtc);
        Assert.Empty(contexts.Identity.DeviceSessions.Where(x => x.UserId == target.Id && x.RevokedOnUtc == null));
    }

    [Theory]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task Reactivate_PartlyVerifiedAdmin_ReturnsActivationNotReady(bool sqlite, bool phoneOnly, bool block)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.SuperAdmin, "مدير", "Admin", "partial.admin@example.test", "+201000001011", UserStatus.Active);
        User target = AddUser(contexts.Identity, AccountType.SupportAdmin, "دعم", "Partial", "partial.target@example.test", "+201000001012", UserStatus.PendingVerification);
        target.SetInitialPasswordHash("test-hash", Now);
        if (phoneOnly)
            target.VerifyPhone(Now);
        else
            target.VerifyEmail(Now);
        await contexts.Identity.SaveChangesAsync();
        var notices = new RecordingNoticePublisher();
        if (block)
            Assert.True((await new BlockAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), notices).Handle(new(actor.Id, target.Id, "Fraud review"), default)).IsSuccess);
        else
            Assert.True((await new SuspendAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), notices).Handle(new(actor.Id, target.Id, "Security review"), default)).IsSuccess);
        int actionsBefore = await contexts.Identity.AdminUserActions.CountAsync(x => x.TargetUserId == target.Id);

        Result<AdminUserStatusResponse> result = await new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(1)), new NeverSeededProtection(), notices).Handle(new(actor.Id, target.Id), default);

        Assert.True(result.IsFailure);
        Assert.Equal(AdminUserErrors.ActivationNotReady.Code, result.Error.Code);
        Assert.Equal(block ? UserStatus.Blocked : UserStatus.Suspended, target.Status);
        Assert.Equal(actionsBefore, await contexts.Identity.AdminUserActions.CountAsync(x => x.TargetUserId == target.Id));
        Assert.Equal(phoneOnly, target.PhoneVerified);
        Assert.Equal(!phoneOnly, target.EmailVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reactivate_ReadinessEdges_MatchDomainRules(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.SuperAdmin, "مدير", "Admin", "edge.admin@example.test", "+201000001021", UserStatus.Active);
        await contexts.Identity.SaveChangesAsync();
        var notices = new RecordingNoticePublisher();
        static async Task<string> ReactivateCode(TestContexts contexts, RecordingNoticePublisher notices, User actor, User target)
        {
            if (target.Status == UserStatus.PendingVerification)
            {
                Result<AdminUserStatusResponse> first = target.Status == UserStatus.Blocked
                    ? throw new InvalidOperationException("test setup error")
                    : await new SuspendAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), notices).Handle(new(actor.Id, target.Id, "Review"), default);
                Assert.True(first.IsSuccess);
            }
            Result<AdminUserStatusResponse> result = await new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(1)), new NeverSeededProtection(), notices).Handle(new(actor.Id, target.Id), default);
            return result.IsFailure ? result.Error.Code : "Success";
        }

        User noAccount = User.Create(FullName.Create("بدون"), FullName.Create("NoAcct"), Email.Create("noacct@example.test"), PhoneNumber.Create("+201000001022"));
        noAccount.SetInitialPasswordHash("test-hash", Now);
        noAccount.VerifyEmail(Now);
        noAccount.VerifyPhone(Now);
        contexts.Identity.Users.Add(noAccount);
        await contexts.Identity.SaveChangesAsync();
        Assert.Equal(AdminUserErrors.ActivationNotReady.Code, await ReactivateCode(contexts, notices, actor, noAccount));

        User noEmail = User.Create(FullName.Create("بدون"), FullName.Create("NoEmail"), null, PhoneNumber.Create("+201000001023"));
        noEmail.AddAccount(AccountType.Family);
        noEmail.SetInitialPasswordHash("test-hash", Now);
        noEmail.VerifyPhone(Now);
        contexts.Identity.Users.Add(noEmail);
        await contexts.Identity.SaveChangesAsync();
        Assert.Equal(AdminUserErrors.ActivationNotReady.Code, await ReactivateCode(contexts, notices, actor, noEmail));

        User noPassword = User.Create(FullName.Create("بدون"), FullName.Create("NoPwd"), Email.Create("nopwd@example.test"), PhoneNumber.Create("+201000001024"));
        noPassword.AddAccount(AccountType.Family);
        noPassword.VerifyEmail(Now);
        noPassword.VerifyPhone(Now);
        contexts.Identity.Users.Add(noPassword);
        await contexts.Identity.SaveChangesAsync();
        Assert.Equal(AdminUserErrors.ActivationNotReady.Code, await ReactivateCode(contexts, notices, actor, noPassword));

        User elderlyOnly = User.Create(FullName.Create("كبير"), FullName.Create("Elder"), null, PhoneNumber.Create("+201000001025"));
        elderlyOnly.AddAccount(AccountType.Elderly);
        elderlyOnly.VerifyPhone(Now);
        contexts.Identity.Users.Add(elderlyOnly);
        await contexts.Identity.SaveChangesAsync();
        Assert.Equal("Success", await ReactivateCode(contexts, notices, actor, elderlyOnly));
        Assert.Equal(UserStatus.Active, elderlyOnly.Status);
        Assert.Null(elderlyOnly.StatusReason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reactivate_VerifiedSuspendedAndBlocked_SucceedWithSingleAuditAndNotice(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.SuperAdmin, "مدير", "Admin", "ok.admin@example.test", "+201000001031", UserStatus.Active);
        User suspended = AddUser(contexts.Identity, AccountType.SupportAdmin, "موقوف", "Susp", "susp.ok@example.test", "+201000001032", UserStatus.Active);
        User blocked = AddUser(contexts.Identity, AccountType.SupportAdmin, "محظور", "Blck", "blck.ok@example.test", "+201000001033", UserStatus.Active);
        await contexts.Identity.SaveChangesAsync();
        var setupNotices = new RecordingNoticePublisher();
        Assert.True((await new SuspendAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), setupNotices).Handle(new(actor.Id, suspended.Id, "Review"), default)).IsSuccess);
        Assert.True((await new BlockAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), setupNotices).Handle(new(actor.Id, blocked.Id, "Incident"), default)).IsSuccess);
        DeviceSession suspendedSession = AddSession(contexts.Identity, suspended.Id, Now, Now.AddDays(1), null);
        suspendedSession.Revoke("Admin suspended the user.", Now);
        DeviceSession blockedSession = AddSession(contexts.Identity, blocked.Id, Now, Now.AddDays(1), null);
        blockedSession.Revoke("Admin blocked the user.", Now);
        await contexts.Identity.SaveChangesAsync();
        var notices = new RecordingNoticePublisher();

        Result<AdminUserStatusResponse> first = await new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(1)), new NeverSeededProtection(), notices).Handle(new(actor.Id, suspended.Id), default);
        Assert.True(first.IsSuccess);
        Assert.Equal(UserStatus.Active, first.Value.Status);
        Assert.Equal(UserStatus.Active, suspended.Status);
        Assert.Null(suspended.StatusReason);
        Assert.Single(notices.Items, n => n.Status == UserStatus.Active);
        Assert.Equal(1, await contexts.Identity.AdminUserActions.CountAsync(x => x.TargetUserId == suspended.Id && x.Action == AdminUserActionType.Reactivated));
        Assert.NotNull(suspendedSession.RevokedOnUtc);
        Assert.Equal(AdminUserErrors.NotSuspended.Code, (await new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(2)), new NeverSeededProtection(), notices).Handle(new(actor.Id, suspended.Id), default)).Error.Code);

        Result<AdminUserStatusResponse> second = await new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(3)), new NeverSeededProtection(), notices).Handle(new(actor.Id, blocked.Id), default);
        Assert.True(second.IsSuccess);
        Assert.Equal(UserStatus.Active, blocked.Status);
        Assert.Null(blocked.StatusReason);
        Assert.Equal(1, await contexts.Identity.AdminUserActions.CountAsync(x => x.TargetUserId == blocked.Id && x.Action == AdminUserActionType.Reactivated));
        Assert.NotNull(blockedSession.RevokedOnUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reactivate_MissingSeededAndInvalidState_KeepExistingCodes(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.SuperAdmin, "مدير", "Admin", "guard.admin@example.test", "+201000001041", UserStatus.Active);
        User seeded = AddUser(contexts.Identity, AccountType.SupportAdmin, "أساسي", "Seeded", "seeded.re@example.test", "+201000001042", UserStatus.PendingVerification);
        User pending = AddUser(contexts.Identity, AccountType.SupportAdmin, "معلق", "Pending", "pending.re@example.test", "+201000001043", UserStatus.PendingVerification);
        User active = AddUser(contexts.Identity, AccountType.SupportAdmin, "نشط", "Active", "active.re@example.test", "+201000001044", UserStatus.Active);
        await contexts.Identity.SaveChangesAsync();
        var handler = new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection(), new RecordingNoticePublisher());
        var seededHandler = new ReactivateAdminUserCommandHandler(contexts.Identity, new FixedClock(Now), new EmailSeedProtection("seeded.re@example.test"), new RecordingNoticePublisher());

        Assert.Equal(AdminUserErrors.NotFound.Code, (await handler.Handle(new(actor.Id, UserId.New()), default)).Error.Code);
        seeded.Suspend("Hold", Now);
        await contexts.Identity.SaveChangesAsync();
        Assert.Equal(AdminUserErrors.SeededProtected.Code, (await seededHandler.Handle(new(actor.Id, seeded.Id), default)).Error.Code);
        Assert.Equal(AdminUserErrors.NotSuspended.Code, (await handler.Handle(new(actor.Id, active.Id), default)).Error.Code);
        Assert.Equal(AdminUserErrors.NotSuspended.Code, (await handler.Handle(new(actor.Id, pending.Id), default)).Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reactivate_PersistenceFailure_IsNotRelabeledActivationNotReady(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.SuperAdmin, "مدير", "Admin", "fail.admin@example.test", "+201000001051", UserStatus.Active);
        User target = AddUser(contexts.Identity, AccountType.SupportAdmin, "مستخدم", "User", "fail.target@example.test", "+201000001052", UserStatus.Active);
        target.Suspend("Temporary", Now);
        await contexts.Identity.SaveChangesAsync();
        var throwing = new ThrowingSaveIdentityDbContext(contexts.Identity);
        var handler = new ReactivateAdminUserCommandHandler(throwing, new FixedClock(Now.AddMinutes(1)), new NeverSeededProtection(), new RecordingNoticePublisher());

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new(actor.Id, target.Id), default));
        Assert.Equal("boom", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccountCommands_ResetChangeAndCreateTranslateOnBothProviders(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User operatorUser = AddUser(contexts.Identity, AccountType.SuperAdmin, "مدير", "Operator", "operator.account@example.test", "+201000000031", UserStatus.Active);
        User target = AddUser(contexts.Identity, AccountType.SupportAdmin, "دعم", "Support", "support.account@example.test", "+201000000032", UserStatus.Active);
        User otherActor = AddUser(contexts.Identity, AccountType.Family, "أسرة", "Other", "other.account@example.test", "+201000000034", UserStatus.Active);
        DeviceSession session = AddSession(contexts.Identity, target.Id, Now, Now.AddDays(1), null);
        await contexts.Identity.SaveChangesAsync();

        var hasher = new TestHasher();
        Result<ResetAdminUserPasswordResponse> reset = await new ResetAdminUserPasswordCommandHandler(contexts.Identity, hasher, new FixedClock(Now), new NeverSeededProtection(), new RecordingNoticePublisher())
            .Handle(new(operatorUser.Id, target.Id), default);
        Assert.True(reset.IsSuccess);
        Assert.True(reset.Value.TemporaryPassword.Length >= 10);
        Assert.Equal(reset.Value.TemporaryPassword, hasher.LastPassword);
        Assert.Equal(1, reset.Value.RevokedSessionCount);
        Assert.NotNull(session.RevokedOnUtc);

        Result<AdminUserAccountTypeResponse> changed = await new ChangeAdminUserAccountTypeCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection())
            .Handle(new(operatorUser.Id, target.Id, AccountType.ContentAdmin), default);
        Assert.True(changed.IsSuccess);
        Assert.Contains(target.Accounts, account => account.AccountType == AccountType.ContentAdmin);
        var changeHandler = new ChangeAdminUserAccountTypeCommandHandler(contexts.Identity, new FixedClock(Now), new NeverSeededProtection());
        Assert.Equal(AdminUserErrors.SelfAction.Code, (await changeHandler.Handle(new(operatorUser.Id, operatorUser.Id, AccountType.SupportAdmin), default)).Error.Code);
        Assert.Equal(AdminUserErrors.LastSuperAdmin.Code, (await changeHandler.Handle(new(otherActor.Id, operatorUser.Id, AccountType.SupportAdmin), default)).Error.Code);

        var mail = new RecordingVerificationSender();
        var sms = new RecordingVerificationSender();
        Result<RegisterUserResponse> created = await new CreateAdminUserCommandHandler(contexts.Identity, new TestHasher(), new TestOtpService(), mail, sms, new FixedClock(Now))
            .Handle(new(operatorUser.Id, "مدير جديد", "New Admin", "new.admin@example.test", "+201000000033", "Strong-Password-123", AccountType.FinanceAdmin), default);
        Assert.True(created.IsSuccess);
        User newAdmin = await contexts.Identity.Users.Include(x => x.Accounts).SingleAsync(x => x.Email == Email.Create("new.admin@example.test"));
        Assert.Equal(UserStatus.PendingVerification, newAdmin.Status);
        Assert.Contains(newAdmin.Accounts, account => account.AccountType == AccountType.FinanceAdmin);
        Assert.Equal(2, await contexts.Identity.VerificationRequests.CountAsync(x => x.UserId == newAdmin.Id));
        Assert.Single(mail.Sent);
        Assert.Single(sms.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymizeAndAuditQueries_TranslateOnBothProviders(bool sqlite)
    {
        await using TestContexts contexts = await TestContexts.CreateAsync(sqlite);
        User actor = AddUser(contexts.Identity, AccountType.SuperAdmin, "أحمد", "Admin", "audit.admin@example.test", "+201000000041", UserStatus.Active);
        User target = AddUser(contexts.Identity, AccountType.Family, "مستخدم", "Target", "audit.target@example.test", "+201000000042", UserStatus.Active);
        DeviceSession session = AddSession(contexts.Identity, target.Id, Now, Now.AddDays(1), null);
        await contexts.Identity.SaveChangesAsync();

        var handler = new AnonymizeAdminUserCommandHandler(contexts.Identity, new FixedClock(Now.AddMinutes(1)), new NeverSeededProtection());
        Result anonymized = await handler.Handle(new(actor.Id, target.Id, "Approved privacy request"), default);
        Assert.True(anonymized.IsSuccess);
        Assert.Null(target.Email);
        Assert.Equal(UserStatus.Blocked, target.Status);
        Assert.NotNull(session.RevokedOnUtc);
        Assert.Equal(AdminUserErrors.AlreadyAnonymized.Code, (await handler.Handle(new(actor.Id, target.Id, "Repeat"), default)).Error.Code);

        AdminUserAuditListResponse audit = (await new AdminUserAuditQueryHandler(contexts.Identity).Handle(
            new(actor.Id.Value.ToString(), target.Id.Value.ToString(), "Anonymized", Now, Now.AddMinutes(2), "Name", 1, 20), default)).Value;
        Assert.Single(audit.Items);
        Assert.Equal(actor.Id.Value, audit.Items[0].ActorUserId);
        Assert.Equal(target.Id.Value, audit.Items[0].TargetUserId);
        Assert.Equal(AdminUserActionType.Anonymized, audit.Items[0].Action);
        Assert.Equal("Approved privacy request", audit.Items[0].Reason);
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

    private sealed class ThrowingSaveIdentityDbContext(IIdentityDbContext inner) : IIdentityDbContext
    {
        public DbSet<User> Users => inner.Users;
        public DbSet<Sanad.Modules.Identity.Domain.Authentication.VerificationRequests.VerificationRequest> VerificationRequests => inner.VerificationRequests;
        public DbSet<DeviceSession> DeviceSessions => inner.DeviceSessions;
        public DbSet<Sanad.Modules.Identity.Domain.Support.SupportTicket> SupportTickets => inner.SupportTickets;
        public DbSet<FeedbackEntity> Feedbacks => inner.Feedbacks;
        public DbSet<AdminUserAction> AdminUserActions => inner.AdminUserActions;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("boom");
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

    private sealed class TestHasher : IPasswordHasher
    {
        public string? LastPassword { get; private set; }
        public string Hash(string password) { LastPassword = password; return "hashed-for-tests"; }
        public PasswordVerificationResult Verify(string passwordHash, string providedPassword) => PasswordVerificationResult.Success;
    }

    private sealed class TestOtpService : IOtpService
    {
        private int _number;
        public GeneratedOtpCode Generate(int length) { _number++; return new GeneratedOtpCode($"1234{_number}", $"hash{_number}"); }
        public bool Verify(string providedCode, string otpHash) => true;
    }

    private sealed class RecordingVerificationSender : IEmailSender, ISmsSender
    {
        public List<string> Sent { get; } = [];
        public Task SendVerificationCodeAsync(string email, string code, Sanad.Modules.Identity.Domain.Authentication.VerificationRequests.VerificationPurpose purpose, CancellationToken cancellationToken) { Sent.Add(email); return Task.CompletedTask; }
        Task ISmsSender.SendVerificationCodeAsync(string phoneNumber, string code, Sanad.Modules.Identity.Domain.Authentication.VerificationRequests.VerificationPurpose purpose, CancellationToken cancellationToken) { Sent.Add(phoneNumber); return Task.CompletedTask; }
        public Task SendEmailAsync(string email, string subject, string body, string? correlationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendFamilyInvitationAsync(string email, string familyName, string inviteLink, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendSupportRequestAsync(string senderName, string? senderEmail, string senderPhoneNumber, string accountTypes, string subject, string message, CancellationToken cancellationToken) => Task.CompletedTask;
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
