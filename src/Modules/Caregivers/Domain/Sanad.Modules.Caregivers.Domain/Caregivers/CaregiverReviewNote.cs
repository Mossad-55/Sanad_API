using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Domain.Caregivers;

/// <summary>
/// E07 append-only caregiver review note. One administrative note written by an
/// administrator about a caregiver: the text, its author and its creation
/// instant are fixed at creation and the type exposes no mutating member, so no
/// API surface this phase adds can rewrite or remove a retained note. Notes are
/// administrative text only — no caregiver identity document, phone number,
/// email address or medical fact is projected by a read.
/// </summary>
public sealed class CaregiverReviewNote : Entity<Guid>
{
    /// <summary>E07 bound on the administrative note text.</summary>
    public const int MaximumTextLength = 2000;

    private CaregiverReviewNote()
    {
    }

    private CaregiverReviewNote(
        Guid id,
        CaregiverId caregiverId,
        UserId authorUserId,
        string text,
        DateTime createdOnUtc)
        : base(id)
    {
        CaregiverId = caregiverId;
        AuthorUserId = authorUserId;
        Text = text;
        CreatedOnUtc = createdOnUtc;
    }

    public CaregiverId CaregiverId { get; private set; }
    public UserId AuthorUserId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DateTime CreatedOnUtc { get; private set; }

    /// <summary>
    /// Writes one note. The text is trimmed and stored verbatim otherwise: no
    /// masking, no truncation and no markdown processing. An empty or
    /// whitespace-only text and a text longer than
    /// <see cref="MaximumTextLength"/> are rejected, so a note row can never
    /// carry a value a client did not really supply.
    /// </summary>
    public static CaregiverReviewNote Write(
        CaregiverId caregiverId,
        UserId authorUserId,
        string? text,
        DateTime createdOnUtc)
    {
        if (caregiverId == CaregiverId.Empty)
            throw new DomainException("A review note requires a caregiver.");
        if (authorUserId == UserId.Empty)
            throw new DomainException("A review note requires an author.");
        if (createdOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Review note timestamps must be UTC.");

        return new CaregiverReviewNote(
            Guid.CreateVersion7(),
            caregiverId,
            authorUserId,
            NormalizeText(text),
            createdOnUtc);
    }

    private static string NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("A review note requires non-whitespace text.");

        string normalized = text.Trim();
        if (normalized.Length > MaximumTextLength)
            throw new DomainException(
                $"A review note cannot exceed {MaximumTextLength} characters.");

        return normalized;
    }
}
