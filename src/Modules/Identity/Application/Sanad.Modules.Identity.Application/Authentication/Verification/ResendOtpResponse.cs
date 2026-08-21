using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Verification;

public sealed record ResendOtpResponse(
    VerificationRequestId VerificationRequestId,
    DateTime ExpiresOnUtc);