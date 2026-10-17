using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Community.Domain.Posts;

/// <summary>
/// Typed identifier for a retained Community post report.
/// </summary>
public readonly record struct CommunityPostReportId(Guid Value)
{
    public static CommunityPostReportId Empty => new(Guid.Empty);
    public static CommunityPostReportId New() => new(Guid.CreateVersion7());
    public override string ToString() => Value.ToString();
}

/// <summary>
/// Lifecycle of a Community post report. A report starts <see cref="Open"/>
/// and leaves that state exactly once, via
/// <see cref="CommunityPostReport.Resolve"/>.
/// </summary>
public enum CommunityReportStatus
{
    Open = 0,
    ActionTaken = 1,
    Dismissed = 2
}

/// <summary>
/// The closed set of report reason codes approved for FUR-07. Unknown codes
/// are validation errors. <see cref="Other"/> requires a note; every other
/// code accepts an optional note.
/// </summary>
public static class CommunityReportReasonCodes
{
    public const string Spam = "Spam";
    public const string HarassmentOrAbuse = "HarassmentOrAbuse";
    public const string HarmfulOrUnsafeContent = "HarmfulOrUnsafeContent";
    public const string MisleadingOrFalseInformation = "MisleadingOrFalseInformation";
    public const string Other = "Other";

    private static readonly HashSet<string> All =
        new(StringComparer.Ordinal)
        {
            Spam,
            HarassmentOrAbuse,
            HarmfulOrUnsafeContent,
            MisleadingOrFalseInformation,
            Other
        };

    public static bool IsValid(string? reasonCode) =>
        reasonCode is not null && All.Contains(reasonCode);

    public static bool RequiresNote(string? reasonCode) =>
        string.Equals(reasonCode, Other, StringComparison.Ordinal);
}

/// <summary>
/// A retained Community report over a published post. Reports are never
/// deleted: a moderator resolves an open report as
/// <see cref="CommunityReportStatus.ActionTaken"/> or
/// <see cref="CommunityReportStatus.Dismissed"/> with actor/time audit.
/// Resolving a report does not itself publish/reject content or restrict an
/// author — those remain explicit moderation actions.
/// </summary>
public sealed class CommunityPostReport : AggregateRoot<CommunityPostReportId>
{
    public const int MaximumNoteLength = 1000;
    public const int MaximumReasonCodeLength = 50;

    private CommunityPostReport()
    {
    }

    private CommunityPostReport(
        CommunityPostReportId id,
        CommunityPostId postId,
        UserId reporterId,
        string reasonCode,
        string? note,
        DateTime createdOnUtc)
        : base(id)
    {
        PostId = postId;
        ReporterId = reporterId;
        ReasonCode = reasonCode;
        Note = note;
        Status = CommunityReportStatus.Open;
        CreatedOnUtc = createdOnUtc;
    }

    public CommunityPostId PostId { get; private set; }
    public UserId ReporterId { get; private set; }
    public string ReasonCode { get; private set; } = string.Empty;
    public string? Note { get; private set; }
    public CommunityReportStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? ResolvedOnUtc { get; private set; }
    public UserId? ResolvedBy { get; private set; }
    public string? ResolutionNote { get; private set; }

    /// <summary>
    /// Optimistic-resolution token: incremented by every
    /// <see cref="Resolve"/> transition and configured as an EF concurrency
    /// token, so two concurrent resolutions collide in persistence and the
    /// loser surfaces as already resolved instead of resolving twice.
    /// </summary>
    public long Version { get; private set; }

    public static CommunityPostReport Create(
        CommunityPostId postId,
        UserId reporterId,
        string? reasonCode,
        string? note,
        DateTime createdOnUtc)
    {
        if (postId == CommunityPostId.Empty)
            throw new DomainException("A Community report requires a post.");

        if (reporterId == UserId.Empty)
            throw new DomainException("A Community report requires a reporter.");

        if (!CommunityReportReasonCodes.IsValid(reasonCode))
            throw new DomainException("Community report reason code is invalid.");

        string code = reasonCode!;
        string? normalizedNote = NormalizeNote(note);

        if (CommunityReportReasonCodes.RequiresNote(code) && normalizedNote is null)
            throw new DomainException("A note is required for this report reason.");

        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community report timestamps must be UTC.");

        return new CommunityPostReport(
            CommunityPostReportId.New(),
            postId,
            reporterId,
            code,
            normalizedNote,
            createdOnUtc);
    }

    /// <summary>Resolves an open report; any other state is terminal.</summary>
    public void Resolve(
        CommunityReportStatus resolution,
        UserId moderatorId,
        string? note,
        DateTime resolvedOnUtc)
    {
        if (Status != CommunityReportStatus.Open)
            throw new DomainException("Community report is already resolved.");

        if (resolution is not (CommunityReportStatus.ActionTaken
            or CommunityReportStatus.Dismissed))
            throw new DomainException("Community report resolution is invalid.");

        if (moderatorId == UserId.Empty)
            throw new DomainException("A report resolution moderator is required.");

        if (resolvedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Community report timestamps must be UTC.");

        Status = resolution;
        ResolvedBy = moderatorId;
        ResolvedOnUtc = resolvedOnUtc;
        ResolutionNote = NormalizeNote(note);
        Version++;
    }

    private static string? NormalizeNote(string? value)
    {
        string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (normalized?.Length > MaximumNoteLength)
            throw new DomainException(
                $"Community report notes must not exceed {MaximumNoteLength} characters.");
        return normalized;
    }
}
