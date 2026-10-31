namespace Ahlena.API.Controllers.Requests;

public sealed record AdminUserAuditRequest(string? ActorUserId, string? TargetUserId, string? Action, DateTime? FromUtc, DateTime? ToUtc, string? Sort = "Newest", int Page = 1, int PageSize = 20);

public sealed record AdminUserReasonRequest(string Reason);
