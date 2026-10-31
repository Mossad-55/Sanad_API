namespace Ahlena.API.Controllers.Requests;

public sealed record AdminUserSessionsRequest(
    string? AccountType = null,
    string? UserId = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 20);
