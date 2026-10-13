using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Chat.Application.Persistence;

/// <summary>
/// Shared translation for identified persistence conflicts. Only identified
/// unique/concurrency conflicts are translated; arbitrary database errors
/// are never masked and propagate to the caller.
/// </summary>
internal static class ChatPersistenceConflicts
{
    public const int MaxRevisionRetryAttempts = 3;

    public static void DetachAdded<TEntity>(DbContext context)
        where TEntity : class
    {
        foreach (var entry in context.ChangeTracker
                     .Entries<TEntity>()
                     .Where(item => item.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Identifies unique/index violations across providers without masking
    /// anything else: PostgreSQL SQLSTATE 23505, SQLite/SQL Server unique
    /// messages. (EF InMemory does not enforce unique indexes; handler
    /// pre-checks rule there.)
    /// </summary>
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string message = current.Message ?? string.Empty;

            if (message.Contains("23505", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Violation of UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
