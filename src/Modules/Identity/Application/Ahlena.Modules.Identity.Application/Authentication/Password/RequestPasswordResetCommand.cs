using Ahlena.BuildingBlocks.Application.CQRS;

namespace Ahlena.Modules.Identity.Application.Authentication.Password;

public sealed record RequestPasswordResetCommand(
    string Email)
    : ICommand;