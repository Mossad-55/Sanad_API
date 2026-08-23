using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.API.Controllers.Requests;

public sealed record RegisterRequest(
    string ArabicFullName,
    string EnglishFullName,
    string Email,
    string PhoneNumber,
    string Password,
    AccountType AccountType,
    string? AvatarUrl);