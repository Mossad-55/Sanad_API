using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.Registration;

public sealed record RegisterUserCommand(
    string ArabicFullName,
    string EnglishFullName,
    string Email,
    string PhoneNumber,
    string Password,
    AccountType AccountType,
    string? AvatarUrl)
    : ICommand<RegisterUserResponse>;