using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Domain.Users;

public enum AdminUserActionType
{
    Suspended = 1,
    Reactivated = 2,
    Blocked = 3,
    SessionsRevoked = 4,
    PasswordReset = 5,
    AccountTypeChanged = 6,
    Created = 7,
    Anonymized = 8
}

public sealed class AdminUserAction
{
    private AdminUserAction() { }

    private AdminUserAction(Guid id, UserId actorUserId, UserId? targetUserId, AdminUserActionType action, string? reason, DateTime createdOnUtc)
    {
        Id = id;
        ActorUserId = actorUserId;
        TargetUserId = targetUserId;
        Action = action;
        Reason = reason;
        CreatedOnUtc = createdOnUtc;
    }

    public Guid Id { get; private set; }
    public UserId ActorUserId { get; private set; } = UserId.Empty;
    public UserId? TargetUserId { get; private set; }
    public AdminUserActionType Action { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public static AdminUserAction Create(UserId actorUserId, UserId? targetUserId, AdminUserActionType action, string? reason, DateTime createdOnUtc)
    {
        if (actorUserId == UserId.Empty || !Enum.IsDefined(action) || createdOnUtc.Kind != DateTimeKind.Utc || reason?.Length > 1000)
            throw new ArgumentException("Admin action audit data is invalid.");
        return new AdminUserAction(Guid.CreateVersion7(), actorUserId, targetUserId, action, reason, createdOnUtc);
    }
}
