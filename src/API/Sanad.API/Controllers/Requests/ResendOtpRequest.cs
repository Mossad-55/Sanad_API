namespace Sanad.API.Controllers.Requests;

public sealed record ResendOtpRequest(
    Guid VerificationRequestId);