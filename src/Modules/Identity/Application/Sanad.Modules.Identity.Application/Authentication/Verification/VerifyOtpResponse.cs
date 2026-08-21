using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Verification;

public sealed record VerifyOtpResponse(
    UserId UserId,
    bool EmailVerified,
    bool PhoneVerified,
    bool NormalAccessAllowed,
    int AttemptesRemaining);