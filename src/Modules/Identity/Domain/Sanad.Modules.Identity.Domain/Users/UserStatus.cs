namespace Sanad.Modules.Identity.Domain.Users;

public enum UserStatus
{
    PendingVerification = 1,
    Active = 2,
    Suspended = 3,
    Blocked = 4
}