using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Domain.Support;

public sealed class SupportTicketReply :
    Entity<SupportTicketReplyId>
{
    public const int MinimumBodyLength = 1;
    public const int MaximumBodyLength = 2000;

    private SupportTicketReply()
    {
    }

    private SupportTicketReply(
        SupportTicketReplyId id,
        SupportTicketId ticketId,
        UserId actorUserId,
        string body,
        DateTime createdOnUtc)
        : base(id)
    {
        TicketId = ticketId;
        ActorUserId = actorUserId;
        Body = body;
        CreatedOnUtc = createdOnUtc;
    }

    public SupportTicketId TicketId { get; private set; }
    public UserId ActorUserId { get; private set; }
    public string Body { get; private set; } = default!;
    public DateTime CreatedOnUtc { get; private set; }

    public static SupportTicketReply Create(
        SupportTicketId ticketId,
        UserId actorUserId,
        string body,
        DateTime createdOnUtc)
    {
        if (actorUserId == UserId.Empty)
        {
            throw new DomainException("Actor user ID is required.");
        }

        if (createdOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new DomainException("Operation time must be in UTC.");
        }

        string trimmed = body.Trim();

        if (trimmed.Length < MinimumBodyLength ||
            trimmed.Length > MaximumBodyLength)
        {
            throw new DomainException(
                $"Reply body must be between {MinimumBodyLength} and {MaximumBodyLength} characters.");
        }

        return new SupportTicketReply(
            SupportTicketReplyId.New(),
            ticketId,
            actorUserId,
            trimmed,
            createdOnUtc);
    }
}