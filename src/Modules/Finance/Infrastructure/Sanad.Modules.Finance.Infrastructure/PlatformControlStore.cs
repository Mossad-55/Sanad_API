using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Infrastructure;

/// <summary>
/// Persistence boundary for the single platform control record and its
/// append-only audit history. Updates compare the caller's expected version to
/// the stored version before mutating, and the <c>version</c> concurrency token
/// is the database-level guard, so a stale writer can never overwrite newer
/// state. Audit entries are only ever added.
/// </summary>
public sealed class PlatformControlStore(IFinanceDbContext dbContext) : IPlatformControlReader, IPlatformControlWriter
{
    public async Task<PlatformControlSnapshot?> GetAsync(CancellationToken cancellationToken)
    {
        var state = await dbContext.PlatformControlStates
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        return state is null ? null : Map(state);
    }

    public async Task<Result<PlatformControlSnapshot>> UpdateAsync(
        int expectedVersion,
        PlatformControlValues values,
        Guid actorUserId,
        string? internalReason,
        CancellationToken cancellationToken)
    {
        var state = await dbContext.PlatformControlStates
            .SingleOrDefaultAsync(cancellationToken);

        if (state is null)
            return Result<PlatformControlSnapshot>.Failure(PlatformControlErrors.Unavailable);

        if (state.Version != expectedVersion)
            return Result<PlatformControlSnapshot>.Failure(PlatformControlErrors.VersionConflict);

        int previousVersion = state.Version;
        PlatformState beforeState = state.State;
        string? beforeMaintenanceMessageArabic = state.MaintenanceMessageArabic;
        string? beforeMaintenanceMessageEnglish = state.MaintenanceMessageEnglish;
        bool beforeCardEnabled = state.CardEnabled;
        bool beforeWalletEnabled = state.WalletEnabled;
        DateTime occurredOnUtc = DateTime.UtcNow;

        try
        {
            state.Apply(
                values.State,
                values.MaintenanceMessageArabic,
                values.MaintenanceMessageEnglish,
                values.CardEnabled,
                values.WalletEnabled,
                occurredOnUtc);

            dbContext.PlatformControlAuditEntries.Add(
                PlatformControlAuditEntry.Create(
                    actorUserId,
                    previousVersion,
                    state.Version,
                    beforeState,
                    state.State,
                    beforeMaintenanceMessageArabic,
                    state.MaintenanceMessageArabic,
                    beforeMaintenanceMessageEnglish,
                    state.MaintenanceMessageEnglish,
                    beforeCardEnabled,
                    state.CardEnabled,
                    beforeWalletEnabled,
                    state.WalletEnabled,
                    internalReason,
                    occurredOnUtc));

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<PlatformControlSnapshot>.Success(Map(state));
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException ex)
        {
            return Result<PlatformControlSnapshot>.Failure(
                new Error(PlatformControlErrors.Invalid.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PlatformControlSnapshot>.Failure(PlatformControlErrors.VersionConflict);
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains("ux_platform_control_states_version", StringComparison.Ordinal))
        {
            return Result<PlatformControlSnapshot>.Failure(PlatformControlErrors.VersionConflict);
        }
    }

    private static PlatformControlSnapshot Map(PlatformControlState state) =>
        new(
            state.Id,
            new PlatformControlValues(
                state.State,
                state.MaintenanceMessageArabic,
                state.MaintenanceMessageEnglish,
                state.CardEnabled,
                state.WalletEnabled),
            state.Version,
            state.UpdatedOnUtc);
}
