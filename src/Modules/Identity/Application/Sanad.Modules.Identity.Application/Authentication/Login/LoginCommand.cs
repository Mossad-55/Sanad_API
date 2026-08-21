using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Authentication.Login;

public sealed record LoginCommand(
    string Email,
    string Password,
    string DeviceName,
    DevicePlatform DevicePlatform,
    string AppVersion)
    : ICommand<LoginResponse>;