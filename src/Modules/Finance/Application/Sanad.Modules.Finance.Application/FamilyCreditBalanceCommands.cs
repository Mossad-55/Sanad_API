using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Application;

/// <summary>
/// Seam for posting an approved refund award into a family's app credit
/// balance (Owner decision #15: app credit is a real financial capability, not
/// a refund label) and for reading the resulting balance.
/// <para>
/// The balance movement and the append-only ledger entry are expected to be
/// written in the same unit of work so a posted credit always has its
/// attributable <see cref="RefundLedgerEntryType.CreditBalancePosted"/> entry.
/// </para>
/// </summary>
public interface IFamilyCreditBalanceWriter
{
    Task<Result<FamilyCreditBalanceSnapshot>> PostAsync(
        Guid familyId,
        Money amount,
        Guid actorUserId,
        string? evidenceReference,
        CancellationToken cancellationToken);
}

public interface IFamilyCreditBalanceReader
{
    Task<FamilyCreditBalanceSnapshot?> GetAsync(Guid familyId, CancellationToken cancellationToken);
}

/// <summary>Read model for a family's app credit balance.</summary>
public sealed record FamilyCreditBalanceSnapshot(
    Guid FamilyId,
    long BalanceMinorUnits,
    string Currency,
    DateTime UpdatedOnUtc);

public sealed record PostFamilyCreditBalanceCommand(
    Guid FamilyId,
    long AmountMinorUnits,
    Guid ActorUserId,
    string? EvidenceReference = null) : ICommand<FamilyCreditBalanceSnapshot>;

public sealed record GetFamilyCreditBalanceQuery(Guid FamilyId) : IQuery<FamilyCreditBalanceSnapshot?>;

public static class FamilyCreditBalanceErrors
{
    public static readonly Error FamilyRequired =
        new("Finance.Credit.FamilyRequired", "A family account is required.");

    public static readonly Error Conflict =
        new("Finance.Credit.Conflict", "The app credit balance was changed concurrently; retry.");

    public static readonly Error Unknown =
        new("Finance.Credit.Unknown", "The app credit balance operation could not be completed.");
}

public sealed class PostFamilyCreditBalanceCommandHandler(IFamilyCreditBalanceWriter writer)
    : ICommandHandler<PostFamilyCreditBalanceCommand, FamilyCreditBalanceSnapshot>
{
    public async Task<Result<FamilyCreditBalanceSnapshot>> Handle(
        PostFamilyCreditBalanceCommand request,
        CancellationToken ct)
    {
        if (request.FamilyId == Guid.Empty)
            return Result<FamilyCreditBalanceSnapshot>.Failure(FamilyCreditBalanceErrors.FamilyRequired);

        try
        {
            return await writer.PostAsync(
                request.FamilyId,
                Money.FromMinorUnits(request.AmountMinorUnits),
                request.ActorUserId,
                request.EvidenceReference,
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<FamilyCreditBalanceSnapshot>.Failure(FamilyCreditBalanceErrors.Unknown);
        }
    }
}

public sealed class GetFamilyCreditBalanceQueryHandler(IFamilyCreditBalanceReader reader)
    : IQueryHandler<GetFamilyCreditBalanceQuery, FamilyCreditBalanceSnapshot?>
{
    public async Task<Result<FamilyCreditBalanceSnapshot?>> Handle(
        GetFamilyCreditBalanceQuery request,
        CancellationToken ct)
    {
        if (request.FamilyId == Guid.Empty)
            return Result<FamilyCreditBalanceSnapshot?>.Failure(FamilyCreditBalanceErrors.FamilyRequired);

        try
        {
            return Result<FamilyCreditBalanceSnapshot?>.Success(
                await reader.GetAsync(request.FamilyId, ct));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<FamilyCreditBalanceSnapshot?>.Failure(FamilyCreditBalanceErrors.Unknown);
        }
    }
}

/// <summary>
/// Persistence boundary for the app credit balance and the refund ledger it is
/// posted from. Stored in the Finance schema alongside the rest of the money
/// core; the ledger is only ever appended to.
/// </summary>
public sealed class FamilyCreditBalanceStore(IFinanceDbContext dbContext)
    : IFamilyCreditBalanceWriter, IFamilyCreditBalanceReader
{
    public async Task<FamilyCreditBalanceSnapshot?> GetAsync(Guid familyId, CancellationToken cancellationToken)
    {
        var balance = await dbContext.CreditBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.FamilyId.Value == familyId, cancellationToken);

        return balance is null ? null : Map(balance);
    }

    public async Task<Result<FamilyCreditBalanceSnapshot>> PostAsync(
        Guid familyId,
        Money amount,
        Guid actorUserId,
        string? evidenceReference,
        CancellationToken cancellationToken)
    {
        if (familyId == Guid.Empty)
            return Result<FamilyCreditBalanceSnapshot>.Failure(FamilyCreditBalanceErrors.FamilyRequired);

        try
        {
            var balance = await dbContext.CreditBalances
                .SingleOrDefaultAsync(x => x.FamilyId.Value == familyId, cancellationToken);

            if (balance is null)
            {
                balance = CreditBalance.Open(new FamilyId(familyId));
                dbContext.CreditBalances.Add(balance);
            }

            DateTime occurredOnUtc = DateTime.UtcNow;
            Money newBalance = balance.Post(amount, occurredOnUtc);

            // Append the ledger entry for the posted credit. The refund case id
            // is the balance's own id when the caller is not posting against an
            // existing refund case; the Money movement and the ledger entry are
            // written together so a credit is never untraceable. The ledger
            // records the MOVEMENT (the awarded amount), never the resulting
            // balance — NetRecorded (#35) sums movements, not account states.
            RefundLedger ledger = await GetOrOpenLedgerAsync(balance.Id, cancellationToken);

            ledger.PostCredit(
                amount,
                occurredOnUtc,
                actorUserId == Guid.Empty ? (Guid?)null : actorUserId,
                evidenceReference);

            await dbContext.SaveChangesAsync(cancellationToken);

            return Result<FamilyCreditBalanceSnapshot>.Success(Map(balance));
        }
        catch (DomainException ex)
        {
            return Result<FamilyCreditBalanceSnapshot>.Failure(
                new("Finance.Credit.Invalid", ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two concurrent postings raced on the same balance row (the balance
            // is an EF concurrency token); the loser retries with fresh state
            // instead of silently losing its movement (#15, #29).
            return Result<FamilyCreditBalanceSnapshot>.Failure(FamilyCreditBalanceErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<FamilyCreditBalanceSnapshot>.Failure(FamilyCreditBalanceErrors.Unknown);
        }
    }

    private async Task<RefundLedger> GetOrOpenLedgerAsync(Guid refundCaseId, CancellationToken cancellationToken)
    {
        var ledger = await dbContext.RefundLedgers
            .Include(x => x.Entries)
            .SingleOrDefaultAsync(x => x.RefundCaseId == refundCaseId, cancellationToken);

        if (ledger is not null)
            return ledger;

        ledger = RefundLedger.Open(refundCaseId);
        dbContext.RefundLedgers.Add(ledger);
        return ledger;
    }

    private static FamilyCreditBalanceSnapshot Map(CreditBalance balance) =>
        new(
            balance.FamilyId.Value,
            balance.Balance.MinorUnits,
            balance.Currency,
            balance.UpdatedOnUtc);
}
