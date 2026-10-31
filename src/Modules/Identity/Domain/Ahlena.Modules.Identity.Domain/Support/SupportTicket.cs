using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Domain.Support;

public sealed class SupportTicket :
    AggregateRoot<SupportTicketId>
{
    public const int MinimumSubjectLength = 3;
    public const int MaximumSubjectLength = 150;
    public const int MinimumMessageLength = 10;
    public const int MaximumMessageLength = 2000;

    private SupportTicket()
    {
    }

    private SupportTicket(
        SupportTicketId id,
        UserId userId,
        string subject,
        string message,
        SupportTicketStatus status,
        DateTime createdOnUtc,
        DateTime? notifiedOnUtc,
        UserId? statusChangedByUserId,
        DateTime? statusChangedOnUtc)
        : base(id)
    {
        UserId = userId;
        Subject = subject;
        Message = message;
        Status = status;
        CreatedOnUtc = createdOnUtc;
        NotifiedOnUtc = notifiedOnUtc;
        StatusChangedByUserId = statusChangedByUserId;
        StatusChangedOnUtc = statusChangedOnUtc;
    }

    public UserId UserId { get; private set; }
    public string Subject { get; private set; } = default!;
    public string Message { get; private set; } = default!;
    public SupportTicketStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime? NotifiedOnUtc { get; private set; }
    public bool IsNotified => NotifiedOnUtc is not null;
    public UserId? StatusChangedByUserId { get; private set; }
    public DateTime? StatusChangedOnUtc { get; private set; }

    public static SupportTicket Create(
        UserId userId,
        string subject,
        string message,
        DateTime createdOnUtc)
    {
        if (userId == UserId.Empty)
        {
            throw new DomainException(
                "User ID is required.");
        }

        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Operation time must be in UTC.");
        }

        string trimmedSubject = subject.Trim();
        string trimmedMessage = message.Trim();

        if (trimmedSubject.Length < MinimumSubjectLength ||
            trimmedSubject.Length > MaximumSubjectLength)
        {
            throw new DomainException(
                $"Subject must be between {MinimumSubjectLength} and {MaximumSubjectLength} characters.");
        }

        if (trimmedMessage.Length < MinimumMessageLength ||
            trimmedMessage.Length > MaximumMessageLength)
        {
            throw new DomainException(
                $"Message must be between {MinimumMessageLength} and {MaximumMessageLength} characters.");
        }

        return new SupportTicket(
            SupportTicketId.New(),
            userId,
            trimmedSubject,
            trimmedMessage,
            SupportTicketStatus.New,
            createdOnUtc,
            null,
            null,
            null);
    }

    public void MarkNotified(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Operation time must be in UTC.");
        }

        NotifiedOnUtc = utcNow;
    }

    public void ChangeStatus(
        SupportTicketStatus newStatus,
        UserId changedByUserId,
        DateTime utcNow)
    {
        if (changedByUserId == UserId.Empty)
        {
            throw new DomainException("Changed by user ID is required.");
        }

        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("Operation time must be in UTC.");
        }

        Status = newStatus;
        StatusChangedByUserId = changedByUserId;
        StatusChangedOnUtc = utcNow;
    }
}
