using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record StartSocialLoginCommand(
    ExternalLoginProvider Provider,
    string ProviderCredential,
    string DeviceName,
    DevicePlatform DevicePlatform,
    string AppVersion)
    : ICommand<StartSocialLoginResponse>;