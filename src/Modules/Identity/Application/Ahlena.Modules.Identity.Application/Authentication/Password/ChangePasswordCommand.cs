using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Password;

public sealed record ChangePasswordCommand(
    UserId CurrentUserId,
    string CurrentPassword,
    string NewPassword)
    : ICommand;