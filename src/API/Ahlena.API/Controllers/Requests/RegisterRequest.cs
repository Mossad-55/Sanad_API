using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.API.Controllers.Requests;

public sealed record RegisterRequest(
    string ArabicFullName,
    string EnglishFullName,
    string Email,
    string PhoneNumber,
    string Password,
    AccountType AccountType);