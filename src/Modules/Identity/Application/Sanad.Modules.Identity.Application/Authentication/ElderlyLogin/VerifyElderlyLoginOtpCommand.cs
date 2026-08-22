using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Identity.Application.Authentication.Login;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Authentication.ElderlyLogin;

public sealed record VerifyElderlyLoginOtpCommand(
    string PhoneNumber,
    string Code,
    string DeviceName,
    DevicePlatform DevicePlatform,
    string AppVersion)
    : ICommand<LoginResponse>;