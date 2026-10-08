using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

public sealed class CareHomeInternalBookingNote : Entity<Guid>
{
    private CareHomeInternalBookingNote() { }

    private CareHomeInternalBookingNote(Guid id, Guid bookingId, UserId author, string text, DateTime createdOnUtc)
        : base(id)
    {
        BookingId = bookingId;
        Author = author;
        Text = text;
        CreatedOnUtc = createdOnUtc;
    }

    public Guid BookingId { get; private set; }
    public UserId Author { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DateTime CreatedOnUtc { get; private set; }

    public static CareHomeInternalBookingNote Create(Guid bookingId, UserId author, string text, DateTime utcNow)
    {
        if (bookingId == Guid.Empty || author == UserId.Empty || utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A booking, author, and UTC timestamp are required.");
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 4000)
            throw new ArgumentException("A note between 1 and 4000 characters is required.", nameof(text));
        return new(Guid.CreateVersion7(), bookingId, author, text.Trim(), utcNow);
    }
}
