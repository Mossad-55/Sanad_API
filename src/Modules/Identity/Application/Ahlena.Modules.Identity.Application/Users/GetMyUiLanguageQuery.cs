using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Users;

public sealed record GetMyUiLanguageQuery(
    UserId CurrentUserId)
    : IQuery<UiLanguageResponse>;
