using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Verification;

public sealed record VerifyOtpCommand(
    VerificationRequestId VerificationRequestId,
    string Code)
    : ICommand<VerifyOtpResponse>;