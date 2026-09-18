using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Families.Application.Abstractions.Data;

/// <summary>
/// Sole write seam for <see cref="BookingCancellationFact"/>. One append-only method: it tracks the
/// fact on the shared scoped change tracker and performs no reads and no save — the calling handler
/// owns the unit of work, so the booking mutation and its fact commit in a single
/// <c>SaveChangesAsync</c>.
/// </summary>
public interface IBookingCancellationFactRecorder
{
    Task RecordAsync(
        BookingCancellationFact fact,
        CancellationToken cancellationToken = default);
}
