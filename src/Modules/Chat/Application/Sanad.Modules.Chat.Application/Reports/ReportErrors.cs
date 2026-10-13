using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Chat.Application.Reports;

/// <summary>
/// Lane B report failure codes (endpoints 11–15).
/// <para>
/// <see cref="Invalid"/> answers the caller's own malformed input (a reason
/// outside the frozen 1–500 bounds, or an unrecognised <c>?status=</c> queue
/// filter value) as a 400 — it reveals nothing about any conversation.
/// </para>
/// <para>
/// <see cref="NotFound"/> is the privacy 404 of endpoint 11: an unknown or
/// foreign conversation, or a <c>messageId</c> that is unknown or belongs to
/// another conversation, all collapse into it, and the Chat controller forces
/// the same generic "resource not found" detail as every other Chat 404. The
/// admin paths (endpoints 13–15) answer an unknown report with this same
/// code, but there the AdminChatReportsController renders it as a plain 404 —
/// Support is authorised to see reported conversations at all, so no
/// privacy-forcing is needed (or possible: an admin is never a participant).
/// </para>
/// <para>
/// <see cref="InvalidState"/> is the 409 of a double resolve (endpoints
/// 14–15): a report leaves Open exactly once, so dismiss-after-dismiss,
/// uphold-after-dismiss, and the reverse all answer 409 with this code,
/// forced by the admin controller (the shared ResultProblemDetailsMapper has
/// no Chat.* entries and defaults an unmapped code to 400).
/// </para>
/// </summary>
public static class ReportErrors
{
    public static readonly Error Invalid =
        new("Chat.Report.Invalid", "The report request is invalid.");

    public static readonly Error NotFound =
        new("Chat.Report.NotFound", "Report not found.");

    public static readonly Error InvalidState =
        new("Chat.Report.InvalidState", "The report is already resolved.");

    /// <summary>
    /// Bounded-compatibility 409: the legacy array endpoint
    /// (`GET reports/{id}/messages`) serves conversations with at most 1,000
    /// messages; above that bound callers must use the paged endpoint
    /// (`GET reports/{id}/messages/page`), which returns the existing
    /// `MessageCursorPage` shape. Never a truncated-success array.
    /// </summary>
    public static readonly Error HistoryRequiresPagination =
        new(
            "Chat.Report.HistoryRequiresPagination",
            "The reported conversation is too large for the legacy view; use the paged messages endpoint.");
}
