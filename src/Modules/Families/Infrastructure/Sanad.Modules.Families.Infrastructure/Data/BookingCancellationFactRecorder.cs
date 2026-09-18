using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.Modules.Families.Infrastructure.Data;

/// <summary>
/// Tracks a <see cref="BookingCancellationFact"/> on the concrete <see cref="FamiliesDbContext"/>.
/// The container resolves this against the same scoped context instance that handlers use
/// (<c>IFamiliesDbContext</c> is forwarded from the concrete registration), so the fact joins the
/// handler's change tracker and the single <c>SaveChangesAsync</c> in the handler commits booking
/// and fact atomically. No <c>AddAsync</c> and no save here on purpose: synchronous tracking keeps
/// the write intent explicit and the unit of work owned by the caller.
/// </summary>
public sealed class BookingCancellationFactRecorder : IBookingCancellationFactRecorder
{
    private readonly FamiliesDbContext _dbContext;

    public BookingCancellationFactRecorder(FamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task RecordAsync(
        BookingCancellationFact fact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fact);

        _dbContext.BookingCancellationFacts.Add(fact);

        return Task.CompletedTask;
    }
}
