namespace Sanad.API.Controllers.Requests;

// Endpoint 11 — submit a report. No body validation beyond model binding:
// the handler owns every rule, so an out-of-bounds reason answers the
// caller's own 400 (Chat.Report.Invalid), while an unknown/foreign
// conversation or a messageId that is unknown or belongs to another
// conversation collapses into the generic privacy 404
// (Chat.Report.NotFound) and never reveals which ids exist.
public sealed record ChatSubmitReportRequest(
    Guid ConversationId,
    Guid? MessageId,
    string? Reason);
