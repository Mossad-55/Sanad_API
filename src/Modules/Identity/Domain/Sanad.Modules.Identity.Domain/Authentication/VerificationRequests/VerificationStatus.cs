namespace Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;

public enum VerificationStatus
{
    Pending = 1,
    Verified = 2,
    Expired = 3,
    Invalidated = 4
}