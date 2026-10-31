using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Cms.Domain.Landing;

/// <summary>
/// Immutable audit row for landing publish/visibility/delete actions.
/// No update/delete paths exist; rows survive section deletion (no FK).
/// </summary>
public sealed class LandingSectionHistory : AggregateRoot<LandingSectionHistoryId>
{
    public const int MaximumDetailsLength = 500;

    private LandingSectionHistory()
    {
    }

    private LandingSectionHistory(
        LandingSectionHistoryId id,
        LandingSectionId sectionId,
        LandingHistoryAction action,
        UserId actorUserId,
        string? details,
        DateTime createdOnUtc)
        : base(id)
    {
        if (sectionId == LandingSectionId.Empty)
            throw new DomainException("History section ID is required.");
        if (!Enum.IsDefined(action))
            throw new DomainException("History action is invalid.");
        if (actorUserId == UserId.Empty)
            throw new DomainException("History actor is required.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("History timestamps must be UTC.");

        SectionId = sectionId;
        Action = action;
        ActorUserId = actorUserId;
        Details = string.IsNullOrWhiteSpace(details)
            ? null
            : details.Trim().Length > MaximumDetailsLength
                ? throw new DomainException($"History details cannot exceed {MaximumDetailsLength} characters.")
                : details.Trim();
        CreatedOnUtc = createdOnUtc;
    }

    public LandingSectionId SectionId { get; private set; }
    public LandingHistoryAction Action { get; private set; }
    public UserId ActorUserId { get; private set; }
    public string? Details { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public static LandingSectionHistory Create(
        LandingSectionId sectionId,
        LandingHistoryAction action,
        UserId actorUserId,
        string? details = null,
        DateTime? createdOnUtc = null) =>
        new(
            LandingSectionHistoryId.New(),
            sectionId,
            action,
            actorUserId,
            details,
            createdOnUtc ?? DateTime.UtcNow);
}
