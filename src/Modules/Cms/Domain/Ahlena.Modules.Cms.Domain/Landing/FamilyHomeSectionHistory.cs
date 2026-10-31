using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Cms.Domain.Landing;

public sealed class FamilyHomeSectionHistory : AggregateRoot<FamilyHomeSectionHistoryId>
{
    public const int MaximumDetailsLength = 500;

    private FamilyHomeSectionHistory()
    {
    }

    private FamilyHomeSectionHistory(
        FamilyHomeSectionHistoryId id,
        FamilyHomeSectionId sectionId,
        LandingHistoryAction action,
        UserId actorUserId,
        string? details,
        DateTime createdOnUtc)
        : base(id)
    {
        if (sectionId == FamilyHomeSectionId.Empty)
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

    public FamilyHomeSectionId SectionId { get; private set; }
    public LandingHistoryAction Action { get; private set; }
    public UserId ActorUserId { get; private set; }
    public string? Details { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public static FamilyHomeSectionHistory Create(
        FamilyHomeSectionId sectionId,
        LandingHistoryAction action,
        UserId actorUserId,
        string? details = null,
        DateTime? createdOnUtc = null) =>
        new(
            FamilyHomeSectionHistoryId.New(),
            sectionId,
            action,
            actorUserId,
            details,
            createdOnUtc ?? DateTime.UtcNow);
}