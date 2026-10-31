using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ahlena.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Provider-aware uniqueness detection for the idempotency race boundary.
/// <para>
/// The shared unique index on <c>(actor_id, operation, idempotency_key)</c> is
/// the authority that makes concurrent duplicates execute exactly once: the
/// loser receives a unique violation on the same statement that would have
/// written its business row, detaches its pending entities and replays the
/// winner. Detection therefore checks PostgreSQL's SQLSTATE
/// (<c>23505</c>) and, when the caller names the expected index, its
/// constraint name, so an unrelated unique violation is never swallowed. A
/// message fallback covers providers that expose no SQLSTATE metadata (for
/// example the SQLite test harness).
/// </para>
/// </summary>
public static class PostgresErrors
{
    public const string UniqueViolationSqlState = "23505";

    /// <summary>
    /// True when <paramref name="exception"/> (or a wrapped inner exception)
    /// is a unique violation, optionally restricted to
    /// <paramref name="expectedIndex"/>.
    /// </summary>
    public static bool IsUniqueViolation(
        DbUpdateException exception,
        string? expectedIndex = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            if (current is PostgresException postgres)
            {
                return postgres.SqlState == UniqueViolationSqlState &&
                    (expectedIndex is null ||
                     string.Equals(
                         postgres.ConstraintName,
                         expectedIndex,
                         StringComparison.Ordinal));
            }

            if (IsUniqueViolationMessage(current.Message))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUniqueViolationMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Violation of UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase);
    }
}
