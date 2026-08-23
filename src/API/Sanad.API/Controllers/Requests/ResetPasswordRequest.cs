namespace Sanad.API.Controllers.Requests;

public sealed record ResetPasswordRequest(
    string Email,
    string OtpCode,
    string NewPassword);