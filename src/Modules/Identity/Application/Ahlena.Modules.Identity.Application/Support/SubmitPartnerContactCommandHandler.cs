using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Support;

namespace Ahlena.Modules.Identity.Application.Support;

/// <summary>
/// Accepts an anonymous partner contact request into the admin support inbox.
/// <para>
/// The request is persisted through <see cref="IIdentityDbContext"/> as its own
/// aggregate: no user account is created, no identity is faked and the
/// submitted details stay request data.
/// </para>
/// <para>
/// Two minimal anti-spam rules, both on the persisted rows:
/// <list type="bullet">
/// <item><description>
/// <b>Idempotency</b> — the same client key and idempotency key with the same
/// content returns the stored contact (no duplicate row); with different
/// content it is a conflict.
/// </description></item>
/// <item><description>
/// <b>Rate limit</b> — at most
/// <see cref="MaxSubmissionsPerWindowPerClient"/> submissions per client key
/// inside <see cref="RateLimitWindow"/>; the caller is told when to retry.
/// </description></item>
/// </list>
/// </para>
/// </summary>
public sealed class SubmitPartnerContactCommandHandler(
    IIdentityDbContext dbContext,
    IDateTimeProvider clock)
    : ICommandHandler<
        SubmitPartnerContactCommand,
        PartnerContactResponse>
{
    /// <summary>Submissions allowed per client key inside the window.</summary>
    public const int MaxSubmissionsPerWindowPerClient = 5;

    /// <summary>The sliding window of the anti-spam rate limit.</summary>
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);

    public async Task<Result<PartnerContactResponse>> Handle(
        SubmitPartnerContactCommand request,
        CancellationToken cancellationToken)
    {
        string idempotencyKey = request.IdempotencyKey.Trim();
        string clientKey = request.ClientKey.Trim();

        if (idempotencyKey.Length == 0 || clientKey.Length == 0)
        {
            return Result<PartnerContactResponse>.Failure(
                PartnerContactErrors.Invalid);
        }

        // Idempotency: the same key with the same content returns the stored
        // contact instead of creating a second row; the same key with
        // different content is a conflict.
        PartnerContact? existing = await dbContext.PartnerContacts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                contact =>
                    contact.ClientKey == clientKey &&
                    contact.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is not null)
        {
            if (SameContent(existing, request))
            {
                return Result<PartnerContactResponse>.Success(
                    new PartnerContactResponse(
                        existing.Id,
                        existing.CreatedOnUtc));
            }

            return Result<PartnerContactResponse>.Failure(
                PartnerContactErrors.Conflict);
        }

        // Rate limit: a sliding window over the persisted submissions of this
        // client key. The retry delay is the moment the oldest submission in
        // the window leaves it.
        DateTime now = clock.UtcNow;
        DateTime windowStart = now - RateLimitWindow;

        int submissionsInWindow = await dbContext.PartnerContacts
            .CountAsync(
                contact =>
                    contact.ClientKey == clientKey &&
                    contact.CreatedOnUtc > windowStart,
                cancellationToken);

        if (submissionsInWindow >= MaxSubmissionsPerWindowPerClient)
        {
            DateTime? oldestInWindow = await dbContext.PartnerContacts
                .Where(
                    contact =>
                        contact.ClientKey == clientKey &&
                        contact.CreatedOnUtc > windowStart)
                .OrderBy(contact => contact.CreatedOnUtc)
                .Select(contact => (DateTime?)contact.CreatedOnUtc)
                .FirstOrDefaultAsync(cancellationToken);

            TimeSpan retryAfter = oldestInWindow is null
                ? RateLimitWindow
                : (oldestInWindow.Value + RateLimitWindow) - now;

            if (retryAfter < TimeSpan.Zero)
            {
                retryAfter = TimeSpan.Zero;
            }

            return Result<PartnerContactResponse>.Failure(
                PartnerContactErrors.RateLimited(retryAfter));
        }

        PartnerContact contact;
        try
        {
            contact = PartnerContact.Create(
                request.Name,
                request.Company,
                request.Email,
                request.Subject,
                request.Message,
                idempotencyKey,
                clientKey,
                now,
                request.Phone,
                request.CompanyType,
                request.City);
        }
        catch (DomainException)
        {
            return Result<PartnerContactResponse>.Failure(
                PartnerContactErrors.Invalid);
        }

        dbContext.PartnerContacts.Add(contact);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A concurrent identical retry lost the race on the unique
            // (client key, idempotency key) index. Detach the failed row and
            // read the persisted winner: the same content returns the winner's
            // receipt, different content is the idempotency conflict. Any
            // other failure — and a race without a winner — propagates.
            DetachAdded(dbContext);

            PartnerContact? winner = await dbContext.PartnerContacts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    contact =>
                        contact.ClientKey == clientKey &&
                        contact.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (winner is null)
            {
                throw;
            }

            if (!SameContent(winner, request))
            {
                return Result<PartnerContactResponse>.Failure(
                    PartnerContactErrors.Conflict);
            }

            return Result<PartnerContactResponse>.Success(
                new PartnerContactResponse(
                    winner.Id,
                    winner.CreatedOnUtc));
        }

        return Result<PartnerContactResponse>.Success(
            new PartnerContactResponse(
                contact.Id,
                contact.CreatedOnUtc));
    }

    /// <summary>
    /// The same normalized comparison the pre-check uses, so a raced retry and
    /// a sequential retry answer identically.
    /// </summary>
    private static bool SameContent(
        PartnerContact contact,
        SubmitPartnerContactCommand request) =>
        contact.Name == request.Name.Trim() &&
        contact.Company == Normalize(request.Company) &&
        contact.Phone == Normalize(request.Phone) &&
        contact.CompanyType == Normalize(request.CompanyType) &&
        contact.City == Normalize(request.City) &&
        contact.Email == request.Email.Trim() &&
        contact.Subject == Normalize(request.Subject) &&
        contact.Message == request.Message.Trim();

    /// <summary>
    /// Detaches the rows this handler added, so the context can read the
    /// persisted winner after a failed save.
    /// </summary>
    private static void DetachAdded(IIdentityDbContext dbContext)
    {
        if (dbContext is not DbContext context)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker
                     .Entries<PartnerContact>()
                     .Where(item => item.State == EntityState.Added)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    /// <summary>
    /// Identifies a unique/index violation across providers without masking
    /// anything else: PostgreSQL SQLSTATE 23505 and the SQLite/SQL Server
    /// unique messages. Any other database failure is never caught here.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            string message = current.Message ?? string.Empty;

            if (message.Contains("23505", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Violation of UNIQUE KEY constraint", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
