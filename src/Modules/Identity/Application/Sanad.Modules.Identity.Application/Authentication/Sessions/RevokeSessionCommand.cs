using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Sessions;

public sealed record RevokeSessionCommand(
    DeviceSessionId DeviceSessionId,
    UserId CurrentUserId)
    : ICommand;