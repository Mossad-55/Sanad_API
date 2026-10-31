using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Application.Abstractions.Caregivers;

/// <summary>
/// The date-specific availability seam used by booking checkout. It is
/// implemented in the API host over the Caregivers module's shared rule
/// function, so the advisory read and the write gate cannot diverge.
/// </summary>
public interface ICaregiverAvailabilityRules
{
    /// <summary>
    /// True only when <c>[startTime, endTime)</c> is exactly contained in one
    /// interval of the caregiver's declared availability for
    /// <paramref name="date"/> after subtracting active booking overlaps.
    /// </summary>
    Task<bool> IsAvailableAsync(
        CaregiverId caregiverId,
        DateOnly date,
        BookingShiftType shiftType,
        TimeOnly startTime,
        TimeOnly endTime,
        CancellationToken cancellationToken = default);
}
