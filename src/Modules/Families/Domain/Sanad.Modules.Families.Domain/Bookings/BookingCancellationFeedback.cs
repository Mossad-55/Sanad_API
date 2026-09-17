using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Families.Domain.Bookings;

/// <summary>
/// Feedback attached to the cancellation of an accepted booking: a known category and a non-blank
/// note. Required for accepted-booking cancellations only; pre-acceptance cancellations keep their
/// current optional reason behaviour.
/// </summary>
public sealed class BookingCancellationFeedback : ValueObject
{
    private BookingCancellationFeedback()
    {
    }

    private BookingCancellationFeedback(
        BookingCancellationReasonCategory category,
        string note)
    {
        Category = category;
        Note = note;
    }

    public BookingCancellationReasonCategory Category { get; private set; }

    public string Note { get; private set; } = string.Empty;

    public static BookingCancellationFeedback Create(
        BookingCancellationReasonCategory category,
        string? note)
    {
        if (!Enum.IsDefined(category))
            throw new DomainException("Cancellation reason category is unknown.");

        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("Cancellation note is required.");

        string trimmedNote = note.Trim();
        if (trimmedNote.Length > Booking.MaximumReasonLength)
            throw new DomainException(
                $"Cancellation note cannot exceed {Booking.MaximumReasonLength} characters.");

        return new BookingCancellationFeedback(category, trimmedNote);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Category;
        yield return Note;
    }
}
