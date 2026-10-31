using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Ahlena.Modules.Identity.Application.Authentication.Login;

public sealed record LoginCommand(
    string Identifier,
    string Password,
    string DeviceName,
    DevicePlatform DevicePlatform,
    string AppVersion)
    : ICommand<LoginResponse>;
