using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.CareHomes.Domain.Bookings;

public sealed class CareHomeBookingAssignmentHistory : Entity<Guid>
{
    private CareHomeBookingAssignmentHistory() { }

    private CareHomeBookingAssignmentHistory(Guid id, Guid bookingId, Guid? fromRoomId, Guid? fromBedId,
        Guid toRoomId, Guid? toBedId, DateOnly effectiveDate, UserId actor, DateTime occurredOnUtc)
        : base(id)
    {
        BookingId = bookingId;
        FromRoomId = fromRoomId;
        FromBedId = fromBedId;
        ToRoomId = toRoomId;
        ToBedId = toBedId;
        EffectiveDate = effectiveDate;
        Actor = actor;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid BookingId { get; private set; }
    public Guid? FromRoomId { get; private set; }
    public Guid? FromBedId { get; private set; }
    public Guid ToRoomId { get; private set; }
    public Guid? ToBedId { get; private set; }
    public DateOnly EffectiveDate { get; private set; }
    public UserId Actor { get; private set; }
    public DateTime OccurredOnUtc { get; private set; }

    public static CareHomeBookingAssignmentHistory CreateInitial(Guid bookingId, Guid roomId, Guid? bedId, DateOnly effectiveDate, UserId actor, DateTime utcNow) =>
        Create(bookingId, null, null, roomId, bedId, effectiveDate, actor, utcNow);

    public static CareHomeBookingAssignmentHistory CreateTransfer(Guid bookingId, Guid fromRoomId, Guid? fromBedId, Guid toRoomId, Guid? toBedId, DateOnly effectiveDate, UserId actor, DateTime utcNow) =>
        Create(bookingId, fromRoomId, fromBedId, toRoomId, toBedId, effectiveDate, actor, utcNow);

    private static CareHomeBookingAssignmentHistory Create(Guid bookingId, Guid? fromRoomId, Guid? fromBedId, Guid toRoomId, Guid? toBedId, DateOnly effectiveDate, UserId actor, DateTime utcNow)
    {
        if (bookingId == Guid.Empty || toRoomId == Guid.Empty || actor == UserId.Empty || utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("A valid booking assignment history entry is required.");
        return new(Guid.CreateVersion7(), bookingId, fromRoomId, fromBedId, toRoomId, toBedId, effectiveDate, actor, utcNow);
    }
}
