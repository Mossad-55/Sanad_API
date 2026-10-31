namespace Ahlena.API.Controllers.Requests;

public sealed record ChangeSupportTicketStatusRequest(
    int Status);

// Schema policy: `Body` is capped at 1–2000 on the TRIMMED text by
// `SupportTicketReply.Create`, so a raw `[StringLength]` would falsely reject
// previously accepted whitespace-padded bodies and is deliberately NOT
// published; the consumed-text cap stays documented instead.
public sealed record ReplyToSupportTicketRequest(
    string Body);