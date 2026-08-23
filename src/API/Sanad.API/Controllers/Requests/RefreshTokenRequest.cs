namespace Sanad.API.Controllers.Requests;

public sealed record RefreshTokenRequest(
    Guid DeviceSessionId,
    string RefreshToken);