using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Users;

public sealed record UpdateMyProfileResponse(
    bool EmailVerified,
    bool PhoneVerified,
    VerificationRequestId? EmailVerificationRequestId,
    VerificationRequestId? PhoneVerificationRequestId);
