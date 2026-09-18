using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Families.Application.Bookings;

/// <summary>
/// Shared recognition of cancellation-fact persistence failures. Used by the family cancel handler
/// and by the caregiver cancel/decline handlers, so a parallel-cancel or double-decline race maps to
/// the same <c>Bookings.Cancel.AlreadyProcessed</c> outcome on every path instead of each handler
/// re-deriving the constraint check.
/// </summary>
internal static class CancellationPersistenceGuard
{
    /// <summary>
    /// Recognizes the unique violation on <c>families.booking_cancellation_facts</c>
    /// (<c>ux_booking_cancellation_facts_booking</c>). The Application assembly does not reference
    /// Npgsql, so the check reads the inner exception message, where PostgreSQL reports the
    /// constraint name for a unique violation.
    /// </summary>
    internal static bool IsFactUniqueViolation(DbUpdateException exception)
    {
        const string constraintName = "ux_booking_cancellation_facts_booking";

        for (Exception? inner = exception.InnerException;
            inner is not null;
            inner = inner.InnerException)
        {
            if (inner.Message.Contains(constraintName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
