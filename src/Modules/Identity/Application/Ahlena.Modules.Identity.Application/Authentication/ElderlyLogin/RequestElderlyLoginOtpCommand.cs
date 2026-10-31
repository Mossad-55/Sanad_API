using Ahlena.BuildingBlocks.Application.CQRS;

namespace Ahlena.Modules.Identity.Application.Authentication.ElderlyLogin;

public sealed record RequestElderlyLoginOtpCommand(
    string PhoneNumber)
    : ICommand;