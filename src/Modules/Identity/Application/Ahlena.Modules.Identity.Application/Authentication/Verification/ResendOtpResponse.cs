using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Verification;

public sealed record ResendOtpResponse(
    VerificationRequestId VerificationRequestId,
    DateTime ExpiresOnUtc);