namespace Sanad.API.Controllers.Requests;

public sealed record VerifyOtpRequest(
    Guid VerificationRequestId,
    string Code);