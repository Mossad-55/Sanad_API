using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Sessions;

public sealed record GetActiveSessionsQuery(
    UserId CurrentUserId)
    : IQuery<ActiveSessionsResponse>;