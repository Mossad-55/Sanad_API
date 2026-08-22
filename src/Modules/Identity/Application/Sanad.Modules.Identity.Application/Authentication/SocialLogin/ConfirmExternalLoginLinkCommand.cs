using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record ConfirmExternalLoginLinkCommand(
    string OpaqueChallenge,
    string Code,
    string DeviceName,
    DevicePlatform DevicePlatform,
    string AppVersion)
    : ICommand<StartSocialLoginResponse>;
