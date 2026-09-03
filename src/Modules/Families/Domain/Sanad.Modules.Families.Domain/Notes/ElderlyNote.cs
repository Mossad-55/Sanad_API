using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Domain.Notes;

public sealed class ElderlyNote : AggregateRoot<ElderlyNoteId>
{
    public const int MaximumTitleLength = 200;
    public const int MaximumDescriptionLength = 2000;

    private ElderlyNote()
    {
    }

    private ElderlyNote(
        ElderlyNoteId id,
        ElderlyId elderlyId,
        UserId authorUserId,
        string title,
        string description,
        NoteCategory category,
        NotePriority priority)
        : base(id)
    {
        ElderlyId = elderlyId;
        AuthorUserId = authorUserId;
        Title = title;
        Description = description;
        Category = category;
        Priority = priority;

        CreatedOnUtc = DateTime.UtcNow;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public ElderlyId ElderlyId { get; private set; }
    public UserId AuthorUserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public NoteCategory Category { get; private set; }
    public NotePriority Priority { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    public static ElderlyNote Create(
        ElderlyId elderlyId,
        UserId authorUserId,
        string title,
        string description,
        NoteCategory category,
        NotePriority priority)
    {
        Validate(title, description, category, priority);

        return new ElderlyNote(
            ElderlyNoteId.New(),
            elderlyId,
            authorUserId,
            title.Trim(),
            description.Trim(),
            category,
            priority);
    }

    public void Update(
        string title,
        string description,
        NoteCategory category,
        NotePriority priority)
    {
        Validate(title, description, category, priority);

        Title = title.Trim();
        Description = description.Trim();
        Category = category;
        Priority = priority;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private static void Validate(
        string title,
        string description,
        NoteCategory category,
        NotePriority priority)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Note title is required.");
        }

        if (title.Trim().Length > MaximumTitleLength)
        {
            throw new DomainException($"Note title cannot exceed {MaximumTitleLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainException("Note description is required.");
        }

        if (description.Trim().Length > MaximumDescriptionLength)
        {
            throw new DomainException($"Note description cannot exceed {MaximumDescriptionLength} characters.");
        }

        if (!Enum.IsDefined(category))
        {
            throw new DomainException("Note category is invalid.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new DomainException("Note priority is invalid.");
        }
    }
}