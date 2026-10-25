using System.Text.Json;
using Sanad.BuildingBlocks.Application.Idempotency;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Bookings;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;

namespace Sanad.UnitTests.Support;

/// <summary>
/// Serializes with the default <see cref="JsonSerializer"/> options. Handler
/// tests that do not send an <c>Idempotency-Key</c> never call it; replay tests
/// only need a stable, byte-exact body.
/// </summary>
internal sealed class StubIdempotentResponseSnapshotter : IIdempotentResponseSnapshotter
{
    public string Snapshot<T>(T response) => JsonSerializer.Serialize(response);
}

/// <summary>
/// Availability seam double: available by default, or always unavailable.
/// The real rule matrix is exercised by the Caregivers availability tests.
/// </summary>
internal sealed class StubCaregiverAvailabilityRules(bool isAvailable = true)
    : ICaregiverAvailabilityRules
{
    public Task<bool> IsAvailableAsync(
        CaregiverId caregiverId,
        DateOnly date,
        BookingShiftType shiftType,
        TimeOnly startTime,
        TimeOnly endTime,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(isAvailable);
}

/// <summary>
/// Runs the action directly. Handler tests do not exercise a serializable
/// transaction; the real guard is covered by the availability/checkout tests.
/// </summary>
internal sealed class PassThroughCaregiverBookingCheckoutGuard
    : ICaregiverBookingCheckoutGuard
{
    public Task<T> ExecuteAsync<T>(
        CaregiverId caregiverId,
        DateOnly bookingDate,
        Func<Task<T>> action,
        CancellationToken cancellationToken) =>
        action();
}
