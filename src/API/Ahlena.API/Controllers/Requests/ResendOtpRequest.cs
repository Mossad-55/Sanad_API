namespace Ahlena.API.Controllers.Requests;

public sealed record ResendOtpRequest(
    Guid VerificationRequestId);