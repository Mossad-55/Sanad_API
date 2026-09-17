using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Validated cancellation reason. Two shapes only:
/// <list type="bullet">
/// <item><description>
/// <see cref="Create"/> — a known category with a non-blank note. This is the feedback an accepted
/// booking requires.
/// </description></item>
/// <item><description>
/// <see cref="CreateOptionalNote"/> — bounded free text with no category, for cancellations that
/// happen before acceptance, where no feedback is mandatory and no category may be invented.
/// </description></item>
/// </list>
/// In both shapes the note is trimmed and limited to <see cref="Booking.MaximumReasonLength"/>
/// characters. Instances are immutable.
/// </summary>
public sealed class BookingCancellationFeedback : ValueObject
{
    private BookingCancellationFeedback()
    {
    }

    private BookingCancellationFeedback(
        BookingCancellationReasonCategory? category,
        string note)
    {
        Category = category;
        Note = note;
    }

    /// <summary>
    /// Known reason category, or null for a note-only pre-acceptance reason. A category is never
    /// defaulted or fabricated.
    /// </summary>
    public BookingCancellationReasonCategory? Category { get; private set; }

    /// <summary>Trimmed note; never blank and never longer than <see cref="Booking.MaximumReasonLength"/>.</summary>
    public string Note { get; private set; } = string.Empty;

    /// <summary>True when a category is attached, i.e. this is accepted-booking feedback.</summary>
    public bool HasCategory => Category is not null;

    /// <summary>
    /// Accepted-booking feedback: a known category plus a non-blank note. Rejects unknown categories,
    /// whitespace-only notes and over-length notes.
    /// </summary>
    public static BookingCancellationFeedback Create(
        BookingCancellationReasonCategory category,
        string? note)
    {
        if (!Enum.IsDefined(category))
            throw new DomainException("Cancellation reason category is unknown.");

        return new BookingCancellationFeedback(category, RequireNote(note));
    }

    /// <summary>
    /// Optional pre-acceptance reason: bounded free text with no category. Blank or whitespace-only
    /// input normalizes to null, so nothing becomes mandatory before acceptance.
    /// </summary>
    public static BookingCancellationFeedback? CreateOptionalNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;

        return new BookingCancellationFeedback(null, RequireNote(note));
    }

    private static string RequireNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("Cancellation note is required.");

        string trimmedNote = note.Trim();
        if (trimmedNote.Length > Booking.MaximumReasonLength)
            throw new DomainException(
                $"Cancellation note cannot exceed {Booking.MaximumReasonLength} characters.");

        return trimmedNote;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Category;
        yield return Note;
    }
}
