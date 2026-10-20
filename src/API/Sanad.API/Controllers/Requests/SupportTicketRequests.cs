namespace Sanad.API.Controllers.Requests;

public sealed record ChangeSupportTicketStatusRequest(
    int Status);

public sealed record ReplyToSupportTicketRequest(
    string Body);