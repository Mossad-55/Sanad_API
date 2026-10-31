using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;

namespace Ahlena.Modules.Chat.Domain.Reports;

/// <summary>
/// User report over a conversation or a single message inside it. Lifecycle
/// is Open → Dismissed (history retained, nothing erased) or Open → Upheld
/// (the handler records the violation and creates the mutual blocks); each
/// report resolves exactly once with actor/time audit.
/// </summary>
public sealed class Report : AggregateRoot<ReportId>
{
    public const int MinimumReasonLength = 1;
    public const int MaximumReasonLength = 500;

    private Report()
    {
    }

    private Report(
        ReportId id,
        ConversationId conversationId,
        MessageId? messageId,
        UserId reporterUserId,
        string reason,
        DateTime createdOnUtc)
        : base(id)
    {
        ConversationId = conversationId;
        MessageId = messageId;
        ReporterUserId = reporterUserId;
        Reason = reason;
        Status = ReportStatus.Open;
        CreatedOnUtc = createdOnUtc;
    }

    public ConversationId ConversationId { get; private set; }

    /// <summary>Optional target message; <see langword="null"/> reports the
    /// conversation as a whole.</summary>
    public MessageId? MessageId { get; private set; }

    public UserId ReporterUserId { get; private set; } = default!;

    public string Reason { get; private set; } = string.Empty;

    public ReportStatus Status { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime? ResolvedOnUtc { get; private set; }

    public UserId? ResolvedBy { get; private set; }

    /// <summary>
    /// Chat Plus optimistic-resolution token: incremented by every
    /// <see cref="Resolve"/> transition and configured as an EF concurrency
    /// token, so two concurrent dismiss/uphold attempts collide in
    /// persistence and the loser surfaces the 409 instead of resolving
    /// twice. Exactly one conditional Open-to-Dismissed/Upheld transition
    /// wins.
    /// </summary>
    public long Version { get; private set; }

    public static Report Create(
        ConversationId conversationId,
        MessageId? messageId,
        UserId reporterUserId,
        string reason,
        DateTime createdOnUtc)
    {
        if (conversationId.Value == Guid.Empty)
        {
            throw new DomainException(
                "Report conversation is required.");
        }

        if (reporterUserId == UserId.Empty)
        {
            throw new DomainException(
                "Report reporter is required.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                "Report reason is required.");
        }

        string normalizedReason = reason.Trim();

        if (normalizedReason.Length > MaximumReasonLength)
        {
            throw new DomainException(
                $"Report reason cannot exceed {MaximumReasonLength} characters.");
        }

        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Report timestamps must be UTC.");
        }

        return new Report(
            ReportId.New(),
            conversationId,
            messageId,
            reporterUserId,
            normalizedReason,
            createdOnUtc);
    }

    /// <summary>Dismisses an open report; history is retained. Any other
    /// state is terminal and rejects the transition.</summary>
    public void Dismiss(UserId actor, DateTime resolvedOnUtc) =>
        Resolve(ReportStatus.Dismissed, actor, resolvedOnUtc);

    /// <summary>Upholds an open report; the handler records the violation
    /// and creates the mutual blocks. Any other state is terminal and
    /// rejects the transition.</summary>
    public void Uphold(UserId actor, DateTime resolvedOnUtc) =>
        Resolve(ReportStatus.Upheld, actor, resolvedOnUtc);

    private void Resolve(ReportStatus status, UserId actor, DateTime resolvedOnUtc)
    {
        if (Status != ReportStatus.Open)
        {
            throw new DomainException(
                "Report is already resolved.");
        }

        if (actor == UserId.Empty)
        {
            throw new DomainException(
                "Report resolution actor is required.");
        }

        if (resolvedOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Report timestamps must be UTC.");
        }

        Status = status;
        ResolvedOnUtc = resolvedOnUtc;
        ResolvedBy = actor;
        Version++;
    }
}
