using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Password;

public sealed record ChangePasswordCommand(
    UserId CurrentUserId,
    string CurrentPassword,
    string NewPassword)
    : ICommand;