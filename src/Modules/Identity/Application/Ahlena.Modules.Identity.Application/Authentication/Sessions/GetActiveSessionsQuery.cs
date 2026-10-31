using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Sessions;

public sealed record GetActiveSessionsQuery(
    UserId CurrentUserId)
    : IQuery<ActiveSessionsResponse>;