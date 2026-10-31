using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Refresh;

public sealed record RefreshTokenCommand(
    DeviceSessionId DeviceSessionId,
    string RefreshToken)
    : ICommand<RefreshTokenResponse>;