using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Verification;

public sealed record VerifyOtpCommand(
    VerificationRequestId VerificationRequestId,
    string Code)
    : ICommand<VerifyOtpResponse>;