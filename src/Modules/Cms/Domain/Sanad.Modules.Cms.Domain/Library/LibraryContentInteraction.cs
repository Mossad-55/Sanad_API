using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Cms.Domain.Library;

public sealed class LibraryContentInteraction : Entity<Guid>
{
    private LibraryContentInteraction()
    {
    }

    private LibraryContentInteraction(
        Guid id,
        LibraryContentId contentId,
        UserId userId,
        LibraryInteractionKind kind,
        DateTime createdOnUtc)
        : base(id)
    {
        ContentId = contentId;
        UserId = userId;
        Kind = kind;
        CreatedOnUtc = createdOnUtc;
    }

    public LibraryContentId ContentId { get; private set; }
    public UserId UserId { get; private set; }
    public LibraryInteractionKind Kind { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }

    public static LibraryContentInteraction Create(
        LibraryContentId contentId,
        UserId userId,
        LibraryInteractionKind kind,
        DateTime createdOnUtc)
    {
        if (contentId == LibraryContentId.Empty)
            throw new DomainException("Library content id is required.");
        if (userId == UserId.Empty)
            throw new DomainException("A library interaction requires a user.");
        if (!Enum.IsDefined(kind))
            throw new DomainException("Library interaction kind is invalid.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Library interaction timestamps must be UTC.");

        return new LibraryContentInteraction(
            Guid.CreateVersion7(), contentId, userId, kind, createdOnUtc);
    }
}

public enum LibraryInteractionKind
{
    Like = 1,
    Save = 2
}
