using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;
using Sanad.Modules.Chat.Domain.Reports;

namespace Sanad.UnitTests.Chat;

/// <summary>
/// Chat Report domain rules: reason length boundary, optional message
/// target, and the single-shot Open → Dismissed / Open → Upheld lifecycle
/// with actor/time audit. Both transitions are one-way; a resolved report
/// never moves again.
/// </summary>
public sealed class ReportTests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 8, 15, 0, 0), DateTimeKind.Utc);

    // ------------------------------------------------------------------
    // Create — reason boundaries.
    // ------------------------------------------------------------------

    [Fact]
    public void Create_AcceptsReasonOfExactlyFiveHundredCharacters()
    {
        string reason = new('r', Report.MaximumReasonLength);

        Report report = CreateReport(reason);

        Assert.Equal(Report.MaximumReasonLength, report.Reason.Length);
        Assert.Equal(ReportStatus.Open, report.Status);
    }

    [Fact]
    public void Create_RejectsReasonOverFiveHundredCharacters()
    {
        string reason = new('r', Report.MaximumReasonLength + 1);

        Assert.Throws<DomainException>(() => CreateReport(reason));
    }

    [Fact]
    public void Create_RejectsEmptyReasonAndTrims()
    {
        Assert.Throws<DomainException>(() => CreateReport(null));
        Assert.Throws<DomainException>(() => CreateReport(string.Empty));
        Assert.Throws<DomainException>(() => CreateReport("   "));

        Report report = CreateReport("  spam  ");
        Assert.Equal("spam", report.Reason);
    }

    // ------------------------------------------------------------------
    // Create — optional message target and required references.
    // ------------------------------------------------------------------

    [Fact]
    public void Create_MessageReferenceIsOptional()
    {
        Report conversationLevel = CreateReport("abuse", messageId: null);
        MessageId messageId = MessageId.New();
        Report messageLevel = CreateReport("abuse", messageId);

        Assert.Null(conversationLevel.MessageId);
        Assert.Equal(messageId, messageLevel.MessageId);
    }

    [Fact]
    public void Create_RejectsMissingConversationOrReporterOrNonUtcStamp()
    {
        UserId reporter = UserId.New();

        Assert.Throws<DomainException>(() => Report.Create(
            ConversationId.Empty, null, reporter, "spam", UtcStamp));
        Assert.Throws<DomainException>(() => Report.Create(
            ConversationId.New(), null, UserId.Empty, "spam", UtcStamp));
        Assert.Throws<DomainException>(() => Report.Create(
            ConversationId.New(), null, reporter, "spam", DateTime.Now));
        Assert.Throws<DomainException>(() => Report.Create(
            ConversationId.New(), null, reporter, "spam",
            new DateTime(2026, 10, 8, 15, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Create_LeavesResolutionFieldsNullAndStampUtc()
    {
        Report report = CreateReport("spam");

        Assert.Equal(UtcStamp, report.CreatedOnUtc);
        Assert.Equal(DateTimeKind.Utc, report.CreatedOnUtc.Kind);
        Assert.Null(report.ResolvedOnUtc);
        Assert.Null(report.ResolvedBy);
    }

    // ------------------------------------------------------------------
    // Lifecycle — one-way transitions with audit.
    // ------------------------------------------------------------------

    [Fact]
    public void Dismiss_ResolvesAnOpenReportAndRecordsTheAudit()
    {
        Report report = CreateReport("not spam after review");
        UserId moderator = UserId.New();
        DateTime resolvedAt = UtcStamp.AddHours(2);

        report.Dismiss(moderator, resolvedAt);

        Assert.Equal(ReportStatus.Dismissed, report.Status);
        Assert.Equal(resolvedAt, report.ResolvedOnUtc);
        Assert.Equal(DateTimeKind.Utc, report.ResolvedOnUtc!.Value.Kind);
        Assert.Equal(moderator, report.ResolvedBy);

        // Dismissal retains the full history — nothing is erased.
        Assert.Equal("not spam after review", report.Reason);
        Assert.Equal(UtcStamp, report.CreatedOnUtc);
    }

    [Fact]
    public void Uphold_ResolvesAnOpenReportAndRecordsTheAudit()
    {
        Report report = CreateReport("harassment");
        UserId moderator = UserId.New();
        DateTime resolvedAt = UtcStamp.AddHours(3);

        report.Uphold(moderator, resolvedAt);

        Assert.Equal(ReportStatus.Upheld, report.Status);
        Assert.Equal(resolvedAt, report.ResolvedOnUtc);
        Assert.Equal(moderator, report.ResolvedBy);
    }

    [Fact]
    public void Resolve_RejectsDoubleResolutionAndKeepsTheFirstOutcome()
    {
        Report dismissed = CreateReport("spam");
        dismissed.Dismiss(UserId.New(), UtcStamp.AddHours(1));
        Assert.Throws<DomainException>(() =>
            dismissed.Dismiss(UserId.New(), UtcStamp.AddHours(2)));

        Report upheld = CreateReport("harassment");
        upheld.Uphold(UserId.New(), UtcStamp.AddHours(1));
        Assert.Throws<DomainException>(() =>
            upheld.Uphold(UserId.New(), UtcStamp.AddHours(2)));

        // The first resolution's audit survives the rejected attempt.
        Assert.Equal(ReportStatus.Dismissed, dismissed.Status);
        Assert.Equal(ReportStatus.Upheld, upheld.Status);
    }

    [Fact]
    public void Resolve_NeverMovesBetweenTerminalStates()
    {
        Report dismissed = CreateReport("spam");
        dismissed.Dismiss(UserId.New(), UtcStamp.AddHours(1));
        Assert.Throws<DomainException>(() =>
            dismissed.Uphold(UserId.New(), UtcStamp.AddHours(2)));

        Report upheld = CreateReport("harassment");
        upheld.Uphold(UserId.New(), UtcStamp.AddHours(1));
        Assert.Throws<DomainException>(() =>
            upheld.Dismiss(UserId.New(), UtcStamp.AddHours(2)));

        Assert.Equal(ReportStatus.Dismissed, dismissed.Status);
        Assert.Equal(ReportStatus.Upheld, upheld.Status);
    }

    [Fact]
    public void Resolve_RejectsEmptyActorAndNonUtcTime()
    {
        Report report = CreateReport("spam");

        Assert.Throws<DomainException>(() =>
            report.Dismiss(UserId.Empty, UtcStamp.AddHours(1)));
        Assert.Throws<DomainException>(() =>
            report.Uphold(UserId.New(), DateTime.Now));
        Assert.Throws<DomainException>(() =>
            report.Uphold(
                UserId.New(),
                new DateTime(2026, 10, 8, 17, 0, 0, DateTimeKind.Unspecified)));

        // A rejected resolution leaves the report open.
        Assert.Equal(ReportStatus.Open, report.Status);
        Assert.Null(report.ResolvedOnUtc);
        Assert.Null(report.ResolvedBy);
    }

    // ------------------------------------------------------------------
    // Harness.
    // ------------------------------------------------------------------

    private static Report CreateReport(
        string? reason,
        MessageId? messageId = null) =>
        Report.Create(
            ConversationId.New(),
            messageId,
            UserId.New(),
            reason!,
            UtcStamp);
}
