namespace Ahlena.API.Controllers.Requests;

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);