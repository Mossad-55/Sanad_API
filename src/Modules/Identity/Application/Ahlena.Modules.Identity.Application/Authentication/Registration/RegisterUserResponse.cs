using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Authentication.Registration;

public sealed record RegisterUserResponse(
    UserId UserId,
    VerificationRequestId EmailVerificationRequestId,
    VerificationRequestId PhoneVerificationRequestId);