using Sanad.BuildingBlocks.Application.CQRS;

namespace Sanad.Modules.Identity.Application.Authentication.Password;

public sealed record ResetPasswordCommand(
    string Email,
    string OtpCode,
    string NewPassword)
    : ICommand;