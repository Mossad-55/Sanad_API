using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Authentication.SocialLogin;

public sealed record RequestSocialRegistrationOtpCommand(
    string OpaqueChallenge,
    string ArabicFullName,
    string EnglishFullName,
    AccountType AccountType,
    string PhoneNumber)
    : ICommand<RequestSocialRegistrationOtpResponse>;