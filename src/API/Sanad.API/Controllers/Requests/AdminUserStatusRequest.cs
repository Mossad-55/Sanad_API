namespace Sanad.API.Controllers.Requests;

public sealed record AdminUserStatusRequest(string? Reason);

public sealed record AdminUserSessionRevokeRequest(Guid? SessionId);
