using Ahlena.Modules.Chat.Domain.Reports;

namespace Ahlena.Modules.Chat.Application.Reports;

/// <summary>
/// Endpoint 11's 201 body: the frozen contract returns only the new
/// report's id.
/// </summary>
public sealed record SubmitReportResponse(Guid ReportId);

/// <summary>
/// The report wire shape, shared by the support queue (endpoint 12) and the
/// detail view (endpoint 13): the stored report fields plus the lifecycle
/// status and the resolution audit (actor and time) once Support closed it.
/// One shape means a row the queue shows is exactly what the detail expands,
/// and the retained reason/history is visible on both.
/// </summary>
public sealed record ReportResponse(
    Guid ReportId,
    Guid ConversationId,
    Guid? MessageId,
    Guid ReporterUserId,
    string Reason,
    string Status,
    DateTime CreatedOnUtc,
    DateTime? ResolvedOnUtc,
    Guid? ResolvedBy);

/// <summary>
/// Maps a persisted <see cref="Report"/> onto the frozen wire shape. Ids are
/// projected in memory (strongly-typed id members do not translate to SQL),
/// which is also why both read handlers load rows instead of a server-side
/// projection.
/// </summary>
internal static class ReportItemFactory
{
    public static ReportResponse Create(Report report) =>
        new(
            report.Id.Value,
            report.ConversationId.Value,
            report.MessageId is { } messageId ? messageId.Value : null,
            report.ReporterUserId.Value,
            report.Reason,
            report.Status.ToString(),
            report.CreatedOnUtc,
            report.ResolvedOnUtc,
            report.ResolvedBy is { } resolvedBy ? resolvedBy.Value : null);
}
