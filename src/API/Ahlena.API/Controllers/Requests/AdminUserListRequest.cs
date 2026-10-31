namespace Ahlena.API.Controllers.Requests;

public sealed record AdminUserListRequest(
    string? Search = null,
    string? AccountType = null,
    string? Status = null,
    string? Sort = null,
    int Page = 1,
    int PageSize = 20);
