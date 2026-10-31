using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.Modules.Identity.Application.Authentication.Login;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Ahlena.Modules.Identity.Application.Authentication.ElderlyLogin;

public sealed record VerifyElderlyLoginOtpCommand(
    string PhoneNumber,
    string Code,
    string DeviceName,
    DevicePlatform DevicePlatform,
    string AppVersion)
    : ICommand<LoginResponse>;