using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record MyProfileResponse(
    string ArabicFullName,
    string EnglishFullName,
    string? Email,
    string PhoneNumber,
    AccountType? AccountType,
    bool EmailVerified,
    bool PhoneVerified,
    string? AvatarUrl);
