using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record UpdateMyProfileResponse(
    bool EmailVerified,
    bool PhoneVerified,
    VerificationRequestId? EmailVerificationRequestId,
    VerificationRequestId? PhoneVerificationRequestId);
