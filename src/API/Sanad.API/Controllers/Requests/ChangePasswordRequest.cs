namespace Sanad.API.Controllers.Requests;

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword);