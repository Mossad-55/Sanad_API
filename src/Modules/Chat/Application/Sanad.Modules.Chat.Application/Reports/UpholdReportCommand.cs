using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Reports;

namespace Sanad.Modules.Chat.Application.Reports;

/// <summary>
/// Endpoint 15 — Support upholds a report (204): the violation is recorded
/// (status + actor/time audit) and a MUTUAL chat-block is applied between
/// the reported conversation's two participants, one <see cref="Block"/> row
/// per direction. Both directions are created with the same idempotent
/// pre-check the block endpoint uses, so a party who already blocked the
/// other never produces a duplicate row (the unique pair index is the
/// database backstop).
/// <para>
/// Ordering is deliberate: the Open-only domain guard runs BEFORE any block
/// is created, and everything lands in one save — a rejected re-uphold can
/// never leave a partial block behind, and the outcome is atomic.
/// </para>
/// </summary>
public sealed record UpholdReportCommand(
    UserId Admin,
    ReportId ReportId)
    : ICommand;

public sealed class UpholdReportCommandHandler
    : ICommandHandler<UpholdReportCommand>
{
    private readonly IChatDbContext _dbContext;

    public UpholdReportCommandHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        UpholdReportCommand request,
        CancellationToken cancellationToken)
    {
        if (request.Admin == UserId.Empty)
        {
            return Result.Failure(ReportErrors.Invalid);
        }

        Report? report = await _dbContext.Reports
            .SingleOrDefaultAsync(
                item => item.Id == request.ReportId,
                cancellationToken);

        if (report is null)
        {
            return Result.Failure(ReportErrors.NotFound);
        }

        Conversation? conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(
                item => item.Id == report.ConversationId,
                cancellationToken);

        // Integrity backstop (a report cascades with its conversation), and
        // the mutual block cannot be built without the pair.
        if (conversation is null)
        {
            return Result.Failure(ReportErrors.NotFound);
        }

        DateTime resolvedOnUtc = DateTime.UtcNow;

        try
        {
            report.Uphold(request.Admin, resolvedOnUtc);
        }
        catch (DomainException)
        {
            // Open-only guard: preconditions pin admin and UTC stamp, so
            // this is a second resolve. No block row is touched on this
            // path — the blocks below only ever run after a first uphold.
            return Result.Failure(ReportErrors.InvalidState);
        }

        UserId participantOne = conversation.ParticipantOne;
        UserId participantTwo = conversation.ParticipantTwo;

        await AddBlockIfMissingAsync(
            participantOne, participantTwo, resolvedOnUtc, cancellationToken);
        await AddBlockIfMissingAsync(
            participantTwo, participantOne, resolvedOnUtc, cancellationToken);

        // One save: the upheld status and both block rows land together or
        // not at all.
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent resolve won the version token first: exactly one
            // conditional Open-to-Upheld transition wins with its atomic
            // status/audit/mutual-block commit; the loser is the 409 and
            // leaves no partial block behind (the save never committed).
            // Stale tracking is cleared so the scoped context is never
            // reused with a half-resolved aggregate.
            if (_dbContext is DbContext context)
            {
                context.ChangeTracker.Clear();
            }

            return Result.Failure(ReportErrors.InvalidState);
        }
        catch (DbUpdateException exception) when (
            Persistence.ChatPersistenceConflicts.IsUniqueViolation(exception))
        {
            // A concurrent block insert won the pair race while this
            // resolution was still open: re-read the authoritative state —
            // an open report retries the idempotent block pre-check, a
            // resolved one is the 409.
            if (_dbContext is DbContext context)
            {
                context.ChangeTracker.Clear();
            }

            Report? current = await _dbContext.Reports
                .SingleOrDefaultAsync(
                    item => item.Id == request.ReportId,
                    cancellationToken);

            if (current is null || current.Status != ReportStatus.Open)
            {
                return Result.Failure(ReportErrors.InvalidState);
            }

            return await Handle(request, cancellationToken);
        }

        return Result.Success();
    }

    /// <summary>
    /// Idempotent pre-check in the block endpoint's shape: an existing
    /// (blocker, blocked) row is left exactly as it is.
    /// </summary>
    private async Task AddBlockIfMissingAsync(
        UserId blocker,
        UserId blocked,
        DateTime createdOnUtc,
        CancellationToken cancellationToken)
    {
        bool alreadyBlocked = await _dbContext.Blocks.AnyAsync(
            block =>
                block.BlockerUserId == blocker &&
                block.BlockedUserId == blocked,
            cancellationToken);

        if (alreadyBlocked)
        {
            return;
        }

        _dbContext.Blocks.Add(
            Block.Create(blocker, blocked, createdOnUtc));
    }
}
