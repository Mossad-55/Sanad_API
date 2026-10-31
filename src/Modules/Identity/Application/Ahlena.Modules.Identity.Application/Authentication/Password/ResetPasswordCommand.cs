using Ahlena.BuildingBlocks.Application.CQRS;

namespace Ahlena.Modules.Identity.Application.Authentication.Password;

public sealed record ResetPasswordCommand(
    string Email,
    string OtpCode,
    string NewPassword)
    : ICommand;