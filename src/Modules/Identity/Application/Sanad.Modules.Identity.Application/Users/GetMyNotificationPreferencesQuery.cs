using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record GetMyNotificationPreferencesQuery(
    UserId CurrentUserId)
    : IQuery<NotificationPreferencesResponse>;
