using Sanad.BuildingBlocks.Application.CQRS;

namespace Sanad.Modules.Identity.Application.Authentication.Password;

public sealed record RequestPasswordResetCommand(
    string Email)
    : ICommand;