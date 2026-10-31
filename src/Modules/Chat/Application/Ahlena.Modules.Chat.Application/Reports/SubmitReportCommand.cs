using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Application.RateLimiting;
using Ahlena.Modules.Chat.Domain.Conversations;
using Ahlena.Modules.Chat.Domain.Messages;
using Ahlena.Modules.Chat.Domain.Reports;

namespace Ahlena.Modules.Chat.Application.Reports;

/// <summary>
/// Endpoint 11 — a participant reports a conversation, or one message inside
/// it. <c>MessageId</c> is null to report the conversation as a whole.
/// Order of operations mirrors the send path's gate-then-validate rule: the
/// privacy gates (conversation membership, message ownership) run before the
/// caller's own payload validation, so a caller who is not entitled to the
/// conversation can never distinguish it from an unknown id by probing with
/// a bad reason.
/// </summary>
public sealed record SubmitReportCommand(
    UserId Reporter,
    ConversationId ConversationId,
    MessageId? MessageId,
    string? Reason)
    : ICommand<SubmitReportResponse>;

public sealed class SubmitReportCommandHandler
    : ICommandHandler<SubmitReportCommand, SubmitReportResponse>
{
    private readonly IChatDbContext _dbContext;
    private readonly IChatRateGate? _rateGate;

    public SubmitReportCommandHandler(
        IChatDbContext dbContext,
        IChatRateGate? rateGate = null)
    {
        _dbContext = dbContext;
        _rateGate = rateGate;
    }

    public async Task<Result<SubmitReportResponse>> Handle(
        SubmitReportCommand request,
        CancellationToken cancellationToken)
    {
        if (_rateGate is not null &&
            !_rateGate.TryAcquireReport(
                request.Reporter,
                out TimeSpan retryAfter))
        {
            return Result<SubmitReportResponse>.Failure(
                ChatRateErrors.Reports(retryAfter));
        }
        // Gate 1 — only a participant may report. Unknown and foreign
        // conversations are one generic privacy 404, so this endpoint can
        // never be used to probe which conversation ids exist. Hiding does
        // not matter: the reporter is still a participant, and reporting is
        // exactly how a conversation reaches Support.
        Conversation? conversation = await _dbContext.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.ConversationId,
                cancellationToken);

        if (conversation is null ||
            !conversation.IsParticipant(request.Reporter))
        {
            return Result<SubmitReportResponse>.Failure(
                ReportErrors.NotFound);
        }

        // Gate 2 — a cited message must belong to THIS conversation.
        // Unknown ids, empty guids, and another conversation's message are
        // indistinguishable privacy 404s (endpoint 13 reads the reported
        // conversation, so leaking which ids exist would leak content).
        if (request.MessageId is { } messageId)
        {
            bool messageBelongs = await _dbContext.Messages
                .AsNoTracking()
                .AnyAsync(
                    item =>
                        item.Id == messageId &&
                        item.ConversationId == conversation.Id,
                    cancellationToken);

            if (!messageBelongs)
            {
                return Result<SubmitReportResponse>.Failure(
                    ReportErrors.NotFound);
            }
        }

        // Validate — the reporter's own payload: reason 1–500 after trim.
        string? reason = request.Reason?.Trim();

        if (reason is null ||
            reason.Length < Report.MinimumReasonLength ||
            reason.Length > Report.MaximumReasonLength)
        {
            return Result<SubmitReportResponse>.Failure(
                ReportErrors.Invalid);
        }

        Report report;

        try
        {
            report = Report.Create(
                conversation.Id,
                request.MessageId,
                request.Reporter,
                reason,
                DateTime.UtcNow);
        }
        catch (DomainException)
        {
            // Backstop for any invariant the checks above did not name —
            // a caller mistake must never surface as a 500.
            return Result<SubmitReportResponse>.Failure(
                ReportErrors.Invalid);
        }

        _dbContext.Reports.Add(report);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<SubmitReportResponse>.Success(
            new SubmitReportResponse(report.Id.Value));
    }
}
