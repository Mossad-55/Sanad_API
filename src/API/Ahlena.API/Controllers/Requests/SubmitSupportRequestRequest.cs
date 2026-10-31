namespace Ahlena.API.Controllers.Requests;

public sealed record SubmitSupportRequestRequest(
    string Subject,
    string Message);
