using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record UpdateMyUiLanguageCommand(
    UserId CurrentUserId,
    UiLanguage UiLanguage)
    : ICommand<UiLanguageResponse>;
