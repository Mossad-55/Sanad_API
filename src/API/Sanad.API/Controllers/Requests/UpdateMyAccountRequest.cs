namespace Sanad.API.Controllers.Requests;

public sealed record UpdateMyAccountRequest(
    string? ArabicFullName,
    string? EnglishFullName,
    string? Email,
    string? PhoneNumber);
