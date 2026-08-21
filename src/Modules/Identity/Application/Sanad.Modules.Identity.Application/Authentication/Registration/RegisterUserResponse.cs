using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Authentication.Registration;

public sealed record RegisterUserResponse(
    UserId UserId,
    VerificationRequestId EmailVerificationRequestId,
    VerificationRequestId PhoneVerificationRequestId);