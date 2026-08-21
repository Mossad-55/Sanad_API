using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Refresh;

public sealed record RefreshTokenCommand(
    DeviceSessionId DeviceSessionId,
    string RefreshToken)
    : ICommand<RefreshTokenResponse>;