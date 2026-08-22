using Sanad.BuildingBlocks.Application.CQRS;

namespace Sanad.Modules.Identity.Application.Authentication.ElderlyLogin;

public sealed record RequestElderlyLoginOtpCommand(
    string PhoneNumber)
    : ICommand;