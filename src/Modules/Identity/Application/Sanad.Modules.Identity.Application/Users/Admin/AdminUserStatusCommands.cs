using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users.Admin;

public sealed record AdminUserStatusNotice(
    UserId UserId,
    string? Email,
    string ArabicName,
    string EnglishName,
    UserStatus Status,
    DateTime CreatedOnUtc,
    string EventType = "StatusChanged");

public interface IAdminUserSeedProtection
{
    bool IsSeeded(User user);
}

public interface IAdminUserStatusNoticePublisher
{
    Task PublishAsync(AdminUserStatusNotice notice, CancellationToken cancellationToken);
}

public static partial class AdminUserErrors
{
    public static Error SeededProtected => new("Identity.Admin.SeededProtected", "The seeded administrator is protected.");
    public static Error SelfAction => new("Identity.Admin.SelfAction", "The administrator cannot perform this action on themselves.");
    public static Error LastSuperAdmin => new("Identity.Admin.LastSuperAdmin", "The last active SuperAdmin cannot be blocked.");
    public static Error AlreadySuspended => new("Identity.Admin.AlreadySuspended", "The user is already suspended.");
    public static Error NotSuspended => new("Identity.Admin.NotSuspended", "The user is not suspended.");
    public static Error AlreadyBlocked => new("Identity.Admin.AlreadyBlocked", "The user is already blocked.");
    public static Error ActivationNotReady => new("Identity.Admin.ActivationNotReady", "The user does not meet the activation requirements. Complete the required verification and account setup before reactivation.");
}

public sealed record AdminUserStatusResponse(Guid UserId, UserStatus Status, int RevokedSessionCount);

public sealed record SuspendAdminUserCommand(UserId ActorUserId, UserId TargetUserId, string Reason)
    : ICommand<AdminUserStatusResponse>;

public sealed record ReactivateAdminUserCommand(UserId ActorUserId, UserId TargetUserId)
    : ICommand<AdminUserStatusResponse>;

public sealed record BlockAdminUserCommand(UserId ActorUserId, UserId TargetUserId, string Reason)
    : ICommand<AdminUserStatusResponse>;

public sealed record RevokeAdminUserSessionsCommand(UserId ActorUserId, UserId TargetUserId, DeviceSessionId? SessionId)
    : ICommand;

public sealed class SuspendAdminUserCommandValidator : AbstractValidator<SuspendAdminUserCommand>
{
    public SuspendAdminUserCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(User.MaximumStatusReasonLength);
    }
}

public sealed class ReactivateAdminUserCommandValidator : AbstractValidator<ReactivateAdminUserCommand>
{
    public ReactivateAdminUserCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(UserId.Empty);
    }
}

public sealed class BlockAdminUserCommandValidator : AbstractValidator<BlockAdminUserCommand>
{
    public BlockAdminUserCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(User.MaximumStatusReasonLength);
    }
}

public sealed class RevokeAdminUserSessionsCommandValidator : AbstractValidator<RevokeAdminUserSessionsCommand>
{
    public RevokeAdminUserSessionsCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(UserId.Empty);
    }
}

public sealed class SuspendAdminUserCommandHandler(
    IIdentityDbContext db,
    IDateTimeProvider clock,
    IAdminUserSeedProtection seedProtection,
    IAdminUserStatusNoticePublisher notices)
    : ICommandHandler<SuspendAdminUserCommand, AdminUserStatusResponse>
{
    public Task<Result<AdminUserStatusResponse>> Handle(SuspendAdminUserCommand request, CancellationToken ct) =>
        ChangeAsync(request.ActorUserId, request.TargetUserId, request.Reason, UserStatus.Suspended, clock.UtcNow, ct);

    private async Task<Result<AdminUserStatusResponse>> ChangeAsync(UserId actorId, UserId targetId, string reason, UserStatus targetStatus, DateTime now, CancellationToken ct)
    {
        User? user = await db.Users.Include(x => x.Accounts).SingleOrDefaultAsync(x => x.Id == targetId, ct);
        if (user is null) return AdminUserErrors.NotFound;
        if (seedProtection.IsSeeded(user)) return AdminUserErrors.SeededProtected;
        if (actorId == targetId) return AdminUserErrors.SelfAction;
        if (user.Status == UserStatus.Suspended) return AdminUserErrors.AlreadySuspended;
        if (user.Status == UserStatus.Blocked) return AdminUserErrors.AlreadyBlocked;

        user.Suspend(reason, now);
        int revoked = await RevokeSessionsAsync(targetId, "Admin suspended the user.", now, ct, db);
        db.AdminUserActions.Add(AdminUserAction.Create(actorId, targetId, AdminUserActionType.Suspended, reason, now));
        await db.SaveChangesAsync(ct);
        await notices.PublishAsync(new(targetId, user.Email?.Value, user.ArabicFullName.Value, user.EnglishFullName.Value, user.Status, now), ct);
        return new AdminUserStatusResponse(targetId.Value, user.Status, revoked);
    }

    internal static async Task<int> RevokeSessionsAsync(UserId userId, string reason, DateTime now, CancellationToken ct, IIdentityDbContext context)
    {
        DeviceSession[] sessions = await context.DeviceSessions.Where(x => x.UserId == userId && x.RevokedOnUtc == null).ToArrayAsync(ct);
        foreach (DeviceSession session in sessions) session.Revoke(reason, now);
        return sessions.Length;
    }
}

public sealed class ReactivateAdminUserCommandHandler(
    IIdentityDbContext db,
    IDateTimeProvider clock,
    IAdminUserSeedProtection seedProtection,
    IAdminUserStatusNoticePublisher notices)
    : ICommandHandler<ReactivateAdminUserCommand, AdminUserStatusResponse>
{
    public async Task<Result<AdminUserStatusResponse>> Handle(ReactivateAdminUserCommand request, CancellationToken ct)
    {
        User? user = await db.Users.Include(x => x.Accounts).SingleOrDefaultAsync(x => x.Id == request.TargetUserId, ct);
        if (user is null) return AdminUserErrors.NotFound;
        if (seedProtection.IsSeeded(user)) return AdminUserErrors.SeededProtected;
        if (user.Status == UserStatus.Active) return AdminUserErrors.NotSuspended;
        if (user.Status is not UserStatus.Suspended and not UserStatus.Blocked) return AdminUserErrors.NotSuspended;
        if (!user.IsActivationReady) return AdminUserErrors.ActivationNotReady;

        user.Reactivate(clock.UtcNow);
        db.AdminUserActions.Add(AdminUserAction.Create(request.ActorUserId, request.TargetUserId, AdminUserActionType.Reactivated, null, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        await notices.PublishAsync(new(user.Id, user.Email?.Value, user.ArabicFullName.Value, user.EnglishFullName.Value, user.Status, clock.UtcNow), ct);
        return new AdminUserStatusResponse(user.Id.Value, user.Status, 0);
    }
}

public sealed class BlockAdminUserCommandHandler(
    IIdentityDbContext db,
    IDateTimeProvider clock,
    IAdminUserSeedProtection seedProtection,
    IAdminUserStatusNoticePublisher notices)
    : ICommandHandler<BlockAdminUserCommand, AdminUserStatusResponse>
{
    public async Task<Result<AdminUserStatusResponse>> Handle(BlockAdminUserCommand request, CancellationToken ct)
    {
        User? user = await db.Users.Include(x => x.Accounts).SingleOrDefaultAsync(x => x.Id == request.TargetUserId, ct);
        if (user is null) return AdminUserErrors.NotFound;
        if (seedProtection.IsSeeded(user)) return AdminUserErrors.SeededProtected;
        if (request.ActorUserId == request.TargetUserId) return AdminUserErrors.SelfAction;
        if (user.Status == UserStatus.Blocked) return AdminUserErrors.AlreadyBlocked;
        if (user.Accounts.Any(x => x.AccountType == AccountType.SuperAdmin) && user.Status == UserStatus.Active &&
            await db.Users.CountAsync(x => x.Status == UserStatus.Active && x.Accounts.Any(a => a.AccountType == AccountType.SuperAdmin), ct) <= 1)
            return AdminUserErrors.LastSuperAdmin;

        DateTime now = clock.UtcNow;
        user.Block(request.Reason, now);
        int revoked = await SuspendAdminUserCommandHandler.RevokeSessionsAsync(request.TargetUserId, "Admin blocked the user.", now, ct, db);
        db.AdminUserActions.Add(AdminUserAction.Create(request.ActorUserId, request.TargetUserId, AdminUserActionType.Blocked, request.Reason, now));
        await db.SaveChangesAsync(ct);
        await notices.PublishAsync(new(user.Id, user.Email?.Value, user.ArabicFullName.Value, user.EnglishFullName.Value, user.Status, now), ct);
        return new AdminUserStatusResponse(user.Id.Value, user.Status, revoked);
    }
}

public sealed class RevokeAdminUserSessionsCommandHandler(IIdentityDbContext db, IDateTimeProvider clock)
    : ICommandHandler<RevokeAdminUserSessionsCommand>
{
    public async Task<Result> Handle(RevokeAdminUserSessionsCommand request, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(x => x.Id == request.TargetUserId, ct)) return Result.Failure(AdminUserErrors.NotFound);
        IQueryable<DeviceSession> query = db.DeviceSessions.Where(x => x.UserId == request.TargetUserId && x.RevokedOnUtc == null);
        if (request.SessionId is not null) query = query.Where(x => x.Id == request.SessionId);
        DeviceSession[] sessions = await query.ToArrayAsync(ct);
        if (request.SessionId is not null && sessions.Length == 0) return Result.Failure(AdminUserErrors.NotFound);
        DateTime now = clock.UtcNow;
        foreach (DeviceSession session in sessions) session.Revoke("Revoked by a SuperAdmin.", now);
        db.AdminUserActions.Add(AdminUserAction.Create(request.ActorUserId, request.TargetUserId, AdminUserActionType.SessionsRevoked, request.SessionId is null ? "All sessions" : "Single session", now));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
