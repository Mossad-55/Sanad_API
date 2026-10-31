using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Verification;

public sealed record ResendOtpCommand(
    VerificationRequestId VerificationRequestId)
    : ICommand<ResendOtpResponse>;