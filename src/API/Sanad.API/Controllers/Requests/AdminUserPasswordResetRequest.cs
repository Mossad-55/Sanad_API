namespace Sanad.API.Controllers.Requests;

public sealed record AdminUserPasswordResetRequest;

public sealed record AdminUserAccountTypeChangeRequest(string AccountType, string? Reason = null);

public sealed record AdminUserCreateRequest(
    string ArabicFullName,
    string EnglishFullName,
    string Email,
    string PhoneNumber,
    string Password,
    string AccountType);
