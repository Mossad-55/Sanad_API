using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Authentication.Registration;

public sealed record RegisterUserCommand(
    string ArabicFullName,
    string EnglishFullName,
    string Email,
    string PhoneNumber,
    string Password,
    AccountType AccountType)
    : ICommand<RegisterUserResponse>;