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

public static partial class AdminUserErrors
{
    public static Error AlreadyAnonymized => new("Identity.Admin.AlreadyAnonymized", "The user is already anonymized.");
}

public sealed record AnonymizeAdminUserCommand(UserId ActorUserId, UserId TargetUserId, string Reason) : ICommand;
public sealed record AdminUserAuditQuery(string? ActorUserId, string? TargetUserId, string? Action, DateTime? FromUtc, DateTime? ToUtc, string? Sort, int Page, int PageSize) : IQuery<AdminUserAuditListResponse>;
public sealed record AdminUserAuditListResponse(IReadOnlyList<AdminUserAuditListItem> Items, int Page, int PageSize, int TotalCount);
public sealed record AdminUserAuditListItem(Guid Id, Guid ActorUserId, Guid? TargetUserId, AdminUserActionType Action, string? Reason, DateTime CreatedOnUtc);

public sealed class AnonymizeAdminUserCommandValidator : AbstractValidator<AnonymizeAdminUserCommand>
{
    public AnonymizeAdminUserCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

public sealed class AdminUserAuditQueryValidator : AbstractValidator<AdminUserAuditQuery>
{
    public AdminUserAuditQueryValidator() { }
}

public sealed class AnonymizeAdminUserCommandHandler(IIdentityDbContext db, IDateTimeProvider clock, IAdminUserSeedProtection seedProtection)
    : ICommandHandler<AnonymizeAdminUserCommand>
{
    public async Task<Result> Handle(AnonymizeAdminUserCommand request, CancellationToken ct)
    {
        User? user = await db.Users.Include(x => x.Accounts).SingleOrDefaultAsync(x => x.Id == request.TargetUserId, ct);
        if (user is null) return Result.Failure(AdminUserErrors.NotFound);
        if (request.ActorUserId == request.TargetUserId) return Result.Failure(AdminUserErrors.SelfAction);
        if (seedProtection.IsSeeded(user)) return Result.Failure(AdminUserErrors.SeededProtected);
        if (user.Email is null && user.Status == UserStatus.Blocked && user.ArabicFullName.Value == "محذوف" && user.EnglishFullName.Value == "Deleted" && !user.EmailVerified && !user.PhoneVerified)
            return Result.Failure(AdminUserErrors.AlreadyAnonymized);
        if (user.Status == UserStatus.Active && user.Accounts.Any(x => x.AccountType == AccountType.SuperAdmin) &&
            await db.Users.CountAsync(x => x.Status == UserStatus.Active && x.Accounts.Any(a => a.AccountType == AccountType.SuperAdmin), ct) <= 1)
            return Result.Failure(AdminUserErrors.LastSuperAdmin);

        DateTime now = clock.UtcNow;
        user.AnonymizeAndDeactivate(request.Reason, now);
        DeviceSession[] sessions = await db.DeviceSessions.Where(x => x.UserId == user.Id && x.RevokedOnUtc == null).ToArrayAsync(ct);
        foreach (DeviceSession session in sessions) session.Revoke("Admin anonymized the user.", now);
        db.AdminUserActions.Add(AdminUserAction.Create(request.ActorUserId, user.Id, AdminUserActionType.Anonymized, request.Reason, now));
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed class AdminUserAuditQueryHandler(IIdentityDbContext db) : IQueryHandler<AdminUserAuditQuery, AdminUserAuditListResponse>
{
    public async Task<Result<AdminUserAuditListResponse>> Handle(AdminUserAuditQuery request, CancellationToken ct)
    {
        if (!AdminQueryParsing.TryPage(request.Page, request.PageSize, out int page, out int pageSize) ||
            !AdminQueryParsing.TrySort(request.Sort, out AdminUserSort sort) ||
            !TryUserId(request.ActorUserId, out UserId? actorId) || !TryUserId(request.TargetUserId, out UserId? targetId) ||
            !TryAction(request.Action, out AdminUserActionType? action) ||
            request.FromUtc is { Kind: not DateTimeKind.Utc } || request.ToUtc is { Kind: not DateTimeKind.Utc } ||
            request.FromUtc > request.ToUtc)
            return AdminUserErrors.InvalidQuery;

        IQueryable<AdminUserAction> filtered = db.AdminUserActions.AsNoTracking();
        if (actorId is not null) filtered = filtered.Where(x => x.ActorUserId == actorId.Value);
        if (targetId is not null) filtered = filtered.Where(x => x.TargetUserId == targetId);
        if (action is not null) filtered = filtered.Where(x => x.Action == action.Value);
        if (request.FromUtc is DateTime from) filtered = filtered.Where(x => x.CreatedOnUtc >= from);
        if (request.ToUtc is DateTime to) filtered = filtered.Where(x => x.CreatedOnUtc <= to);
        int count = await filtered.CountAsync(ct);
        IQueryable<AdminUserAuditListItem> projected;
        if (sort == AdminUserSort.Name)
        {
            projected = from audit in filtered
                        join actor in db.Users.AsNoTracking() on audit.ActorUserId equals actor.Id
                        orderby EF.Property<string>(actor, nameof(User.ArabicFullName)), EF.Property<string>(actor, nameof(User.EnglishFullName)), audit.CreatedOnUtc descending
                        select new AdminUserAuditListItem(audit.Id, audit.ActorUserId.Value, audit.TargetUserId == null ? null : audit.TargetUserId.Value.Value, audit.Action, audit.Reason, audit.CreatedOnUtc);
        }
        else
        {
            IOrderedQueryable<AdminUserAction> ordered = sort == AdminUserSort.Newest
                ? filtered.OrderByDescending(x => x.CreatedOnUtc).ThenBy(x => x.Id)
                : filtered.OrderBy(x => x.CreatedOnUtc).ThenBy(x => x.Id);
            projected = ordered
                .Select(x => new AdminUserAuditListItem(x.Id, x.ActorUserId.Value, x.TargetUserId == null ? null : x.TargetUserId.Value.Value, x.Action, x.Reason, x.CreatedOnUtc));
        }
        AdminUserAuditListItem[] items = await projected.Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync(ct);
        return new AdminUserAuditListResponse(items, page, pageSize, count);
    }

    private static bool TryUserId(string? value, out UserId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Guid.TryParse(value, out Guid parsed) || parsed == Guid.Empty) return false;
        id = new UserId(parsed);
        return true;
    }

    private static bool TryAction(string? value, out AdminUserActionType? action)
    {
        action = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!Enum.TryParse(value, true, out AdminUserActionType parsed) || !Enum.IsDefined(parsed)) return false;
        action = parsed;
        return true;
    }
}
