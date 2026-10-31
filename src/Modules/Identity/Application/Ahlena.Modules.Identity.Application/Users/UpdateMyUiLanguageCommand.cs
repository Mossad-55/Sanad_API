using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Users;

public sealed record UpdateMyUiLanguageCommand(
    UserId CurrentUserId,
    UiLanguage UiLanguage)
    : ICommand<UiLanguageResponse>;
