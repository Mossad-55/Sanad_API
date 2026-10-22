using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Application;

/// <summary>
/// Stable error codes for the reviewed-complaint workflow (clause 5, Owner
/// decisions #11-#14). Domain guards throw
/// <see cref="DomainException"/>; the application layer maps them to these
/// codes so the API has a stable contract — the same split the credit-balance
/// seams use (<c>Finance.Credit.Invalid</c>).
/// </summary>
public static class ComplaintReviewErrors
{
    public static readonly Error FamilyRequired =
        new("Finance.Complaint.FamilyRequired", "A family account is required.");

    public static readonly Error CaseRequired =
        new("Finance.Complaint.CaseRequired", "A refund case is required.");

    public static readonly Error NotFound =
        new("Finance.Complaint.NotFound", "The complaint review was not found.");

    public static readonly Error Invalid =
        new("Finance.Complaint.Invalid", "The complaint review transition is not valid.");

    /// <summary>
    /// Owner decision #12: only SuperAdmin approves or refuses a reviewed
    /// financial award. SupportAdmin may receive, investigate and read a case but
    /// may never exercise the verdict, so the seam — not only the HTTP policy —
    /// refuses it. A verdict taken by a non-SuperAdmin is <c>403</c>.
    /// </summary>
    public static readonly Error SuperAdminRequired =
        new("Finance.Complaint.SuperAdminRequired", "Only a SuperAdmin can approve or refuse a reviewed complaint award.");

    public static readonly Error AmountInvalid =
        new("Finance.Complaint.AmountInvalid", "The reviewed complaint award amount is invalid.");

    public static readonly Error Conflict =
        new("Finance.Complaint.Conflict", "The complaint review was changed concurrently; retry.");

    public static readonly Error Unknown =
        new("Finance.Complaint.Unknown", "The complaint review operation could not be completed.");
}

/// <summary>
/// Read model for one reviewed complaint case.
/// <para>
/// <see cref="ReviewState"/>, <see cref="Timeliness"/> and the actor/instant
/// pairs are the observable review phases the verification pack reads
/// (<c>finance.complaint_reviews</c>): the case is readable in an
/// <see cref="ComplaintReviewState.Investigating"/> phase attributable to
/// SupportAdmin before any <see cref="RefundLedgerEntryType.Approved"/> entry
/// exists (#12), and an early <see cref="Timeliness.Late"/> complaint is
/// accepted but flagged (#13).
/// </para>
/// </summary>
public sealed record ComplaintReviewSnapshot(
    Guid Id,
    Guid RefundCaseId,
    Guid FamilyId,
    ComplaintReportingParty ReportingParty,
    Guid ReportedByUserId,
    DateTime ReportedOnUtc,
    DateTime? VisitedOnUtc,
    ComplaintTimeliness Timeliness,
    ComplaintReviewState ReviewState,
    bool RequiresLateReview,
    string? Description,
    Guid? InvestigatorUserId,
    DateTime? InvestigationStartedOnUtc,
    string? EvidenceReference,
    string? DecisionNote,
    Guid? ApprovedByUserId,
    DateTime? ApprovedOnUtc,
    Guid? RefusedByUserId,
    DateTime? RefusedOnUtc,
    DateTime? ProcessingDeadlineStartsOnUtc,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

/// <summary>
/// Persistence boundary for reviewed complaint cases. Implemented in
/// Finance.Infrastructure on the Finance schema: compliant with the W3 design
/// pin, no other module writes this table.
/// </summary>
public interface IComplaintReviewStore
{
    Task<Result<ComplaintReviewSnapshot>> OpenAsync(
        Guid refundCaseId,
        Guid familyId,
        ComplaintReportingParty reportingParty,
        Guid reportedByUserId,
        DateTime reportedOnUtc,
        DateTime? visitedOnUtc,
        bool isSafetyConcern,
        string? description,
        CancellationToken cancellationToken);

    Task<Result<ComplaintReviewSnapshot>> BeginInvestigationAsync(
        Guid complaintReviewId,
        Guid investigatorUserId,
        DateTime startedOnUtc,
        string? evidenceReference,
        CancellationToken cancellationToken);

    Task<Result<ComplaintReviewSnapshot>> ApproveAsync(
        Guid complaintReviewId,
        Guid approvingUserId,
        DateTime approvedOnUtc,
        long awardMinorUnits,
        string? decisionNote,
        string? evidenceReference,
        CancellationToken cancellationToken);

    Task<Result<ComplaintReviewSnapshot>> RefuseAsync(
        Guid complaintReviewId,
        Guid refusingUserId,
        DateTime refusedOnUtc,
        string? decisionNote,
        CancellationToken cancellationToken);
}

public interface IComplaintReviewReader
{
    Task<ComplaintReviewSnapshot?> GetAsync(Guid complaintReviewId, CancellationToken cancellationToken);

    Task<ComplaintReviewSnapshot?> FindForCaseAsync(Guid refundCaseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ComplaintReviewSnapshot>> ListAsync(
        ComplaintReviewState? state,
        Guid? familyId,
        CancellationToken cancellationToken);
}

public sealed record OpenComplaintReviewCommand(
    Guid RefundCaseId,
    Guid FamilyId,
    ComplaintReportingParty ReportingParty,
    Guid ReportedByUserId,
    DateTime ReportedOnUtc,
    DateTime? VisitedOnUtc = null,
    bool IsSafetyConcern = false,
    string? Description = null) : ICommand<ComplaintReviewSnapshot>;

/// <summary>
/// Seam-level role check for the review verdict (#12). The HTTP policy already
/// gates the surface to SuperAdmin+SupportAdmin, but a verdict is SuperAdmin-only
/// and that rule is enforced here too, so no other caller — a future job, a
/// direct seam invocation, or a regression in the policy — can approve or refuse
/// an award as SupportAdmin. The actor's account type is carried on the verdict
/// commands as the house <c>ActorAccountType</c> string
/// (<c>nameof(AccountType.SuperAdmin)</c>), never re-derived from a token in the
/// application layer.
/// </summary>
public static class ComplaintReviewRoleGuard
{
    /// <summary>The one account type that may exercise a reviewed verdict (#12).</summary>
    public const string SuperAdminAccountType = "SuperAdmin";

    public static bool IsSuperAdmin(string? actorAccountType) =>
        string.Equals(actorAccountType?.Trim(), SuperAdminAccountType, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// SupportAdmin receives and investigates (Owner decision #12).
/// </summary>
public sealed record BeginComplaintReviewInvestigationCommand(
    Guid ComplaintReviewId,
    Guid InvestigatorUserId,
    DateTime StartedOnUtc,
    string? EvidenceReference = null) : ICommand<ComplaintReviewSnapshot>;

/// <summary>
/// SuperAdmin approves the reviewed award (Owner decision #12) and records the
/// resulting money movement (#16: approval alone is not completion).
/// <para>
/// The award is a pure MOVEMENT record (#35): the ledger entry captures the
/// awarded amount, never a resulting balance. It is appended through the frozen
/// <see cref="RefundLedger"/> under the case's
/// <see cref="ComplaintReviewSnapshot.RefundCaseId"/>. A zero award is valid
/// (#31) and is recorded as computed. The wider decision-#4 ceiling execution
/// (checking the award against all prior awards for the family across cases) is
/// a recorded cross-lane follow-up and is not built here.
/// </para>
/// </summary>
public sealed record ApproveComplaintReviewCommand(
    Guid ComplaintReviewId,
    Guid ApprovingUserId,
    string? ApprovingAccountType,
    DateTime ApprovedOnUtc,
    long AwardMinorUnits = 0,
    string? DecisionNote = null,
    string? EvidenceReference = null) : ICommand<ComplaintReviewSnapshot>;

/// <summary>Owner decision #12: only SuperAdmin refuses a reviewed award; no money moves (#11).</summary>
public sealed record RefuseComplaintReviewCommand(
    Guid ComplaintReviewId,
    Guid RefusingUserId,
    string? RefusingAccountType,
    DateTime RefusedOnUtc,
    string? DecisionNote = null) : ICommand<ComplaintReviewSnapshot>;

public sealed record GetComplaintReviewQuery(Guid ComplaintReviewId) : IQuery<ComplaintReviewSnapshot?>;

public sealed record GetComplaintReviewForCaseQuery(Guid RefundCaseId) : IQuery<ComplaintReviewSnapshot?>;

public sealed record ListComplaintReviewsQuery(
    ComplaintReviewState? State = null,
    Guid? FamilyId = null) : IQuery<IReadOnlyList<ComplaintReviewSnapshot>>;

public sealed class OpenComplaintReviewCommandHandler(IComplaintReviewStore store)
    : ICommandHandler<OpenComplaintReviewCommand, ComplaintReviewSnapshot>
{
    public async Task<Result<ComplaintReviewSnapshot>> Handle(
        OpenComplaintReviewCommand request,
        CancellationToken cancellationToken)
    {
        if (request.RefundCaseId == Guid.Empty)
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.CaseRequired);
        if (request.FamilyId == Guid.Empty)
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.FamilyRequired);

        return await store.OpenAsync(
            request.RefundCaseId,
            request.FamilyId,
            request.ReportingParty,
            request.ReportedByUserId,
            request.ReportedOnUtc,
            request.VisitedOnUtc,
            request.IsSafetyConcern,
            request.Description,
            cancellationToken);
    }
}

public sealed class BeginComplaintReviewInvestigationCommandHandler(IComplaintReviewStore store)
    : ICommandHandler<BeginComplaintReviewInvestigationCommand, ComplaintReviewSnapshot>
{
    public Task<Result<ComplaintReviewSnapshot>> Handle(
        BeginComplaintReviewInvestigationCommand request,
        CancellationToken cancellationToken) =>
        store.BeginInvestigationAsync(
            request.ComplaintReviewId,
            request.InvestigatorUserId,
            request.StartedOnUtc,
            request.EvidenceReference,
            cancellationToken);
}

public sealed class ApproveComplaintReviewCommandHandler(IComplaintReviewStore store)
    : ICommandHandler<ApproveComplaintReviewCommand, ComplaintReviewSnapshot>
{
    public async Task<Result<ComplaintReviewSnapshot>> Handle(
        ApproveComplaintReviewCommand request,
        CancellationToken cancellationToken)
    {
        // Seam-level #12 role split: a verdict is SuperAdmin-only, even though
        // the HTTP policy also admits SupportAdmin for the investigate/read
        // surface.
        if (!ComplaintReviewRoleGuard.IsSuperAdmin(request.ApprovingAccountType))
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.SuperAdminRequired);

        if (request.AwardMinorUnits < 0)
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.AmountInvalid);

        return await store.ApproveAsync(
            request.ComplaintReviewId,
            request.ApprovingUserId,
            request.ApprovedOnUtc,
            request.AwardMinorUnits,
            request.DecisionNote,
            request.EvidenceReference,
            cancellationToken);
    }
}

public sealed class RefuseComplaintReviewCommandHandler(IComplaintReviewStore store)
    : ICommandHandler<RefuseComplaintReviewCommand, ComplaintReviewSnapshot>
{
    public async Task<Result<ComplaintReviewSnapshot>> Handle(
        RefuseComplaintReviewCommand request,
        CancellationToken cancellationToken)
    {
        if (!ComplaintReviewRoleGuard.IsSuperAdmin(request.RefusingAccountType))
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.SuperAdminRequired);

        return await store.RefuseAsync(
            request.ComplaintReviewId,
            request.RefusingUserId,
            request.RefusedOnUtc,
            request.DecisionNote,
            cancellationToken);
    }
}

public sealed class GetComplaintReviewQueryHandler(IComplaintReviewReader reader)
    : IQueryHandler<GetComplaintReviewQuery, ComplaintReviewSnapshot?>
{
    public async Task<Result<ComplaintReviewSnapshot?>> Handle(
        GetComplaintReviewQuery request,
        CancellationToken cancellationToken) =>
        Result<ComplaintReviewSnapshot?>.Success(
            await reader.GetAsync(request.ComplaintReviewId, cancellationToken));
}

public sealed class GetComplaintReviewForCaseQueryHandler(IComplaintReviewReader reader)
    : IQueryHandler<GetComplaintReviewForCaseQuery, ComplaintReviewSnapshot?>
{
    public async Task<Result<ComplaintReviewSnapshot?>> Handle(
        GetComplaintReviewForCaseQuery request,
        CancellationToken cancellationToken) =>
        Result<ComplaintReviewSnapshot?>.Success(
            await reader.FindForCaseAsync(request.RefundCaseId, cancellationToken));
}

public sealed class ListComplaintReviewsQueryHandler(IComplaintReviewReader reader)
    : IQueryHandler<ListComplaintReviewsQuery, IReadOnlyList<ComplaintReviewSnapshot>>
{
    public async Task<Result<IReadOnlyList<ComplaintReviewSnapshot>>> Handle(
        ListComplaintReviewsQuery request,
        CancellationToken cancellationToken) =>
        Result<IReadOnlyList<ComplaintReviewSnapshot>>.Success(
            await reader.ListAsync(request.State, request.FamilyId, cancellationToken));
}

/// <summary>
/// The Finance-side persistence implementation over <see cref="IFinanceDbContext"/>.
/// One reviewed complaint case per refund case (unique index), so a case's
/// review and its ledger are joined by <c>refund_case_id</c> without a
/// cross-context relationship.
/// </summary>
public sealed class ComplaintReviewStore(IFinanceDbContext dbContext)
    : IComplaintReviewStore, IComplaintReviewReader
{
    public async Task<ComplaintReviewSnapshot?> GetAsync(Guid complaintReviewId, CancellationToken cancellationToken)
    {
        ComplaintReview? review = await dbContext.ComplaintReviews
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == complaintReviewId, cancellationToken);

        return review is null ? null : Map(review);
    }

    public async Task<ComplaintReviewSnapshot?> FindForCaseAsync(Guid refundCaseId, CancellationToken cancellationToken)
    {
        ComplaintReview? review = await dbContext.ComplaintReviews
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.RefundCaseId == refundCaseId, cancellationToken);

        return review is null ? null : Map(review);
    }

    public async Task<IReadOnlyList<ComplaintReviewSnapshot>> ListAsync(
        ComplaintReviewState? state,
        Guid? familyId,
        CancellationToken cancellationToken)
    {
        IQueryable<ComplaintReview> query = dbContext.ComplaintReviews.AsNoTracking();
        if (state is not null)
            query = query.Where(x => x.State == state.Value);
        if (familyId is not null && familyId.Value != Guid.Empty)
            query = query.Where(x => x.FamilyId == familyId.Value);

        List<ComplaintReview> reviews = await query
            .OrderByDescending(x => x.ReportedOnUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        return reviews.Select(Map).ToList();
    }

    public async Task<Result<ComplaintReviewSnapshot>> OpenAsync(
        Guid refundCaseId,
        Guid familyId,
        ComplaintReportingParty reportingParty,
        Guid reportedByUserId,
        DateTime reportedOnUtc,
        DateTime? visitedOnUtc,
        bool isSafetyConcern,
        string? description,
        CancellationToken cancellationToken)
    {
        try
        {
            bool caseAlreadyReviewed = await dbContext.ComplaintReviews
                .AnyAsync(x => x.RefundCaseId == refundCaseId, cancellationToken);
            if (caseAlreadyReviewed)
                return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.Conflict);

            ComplaintReview review = ComplaintReview.Open(
                refundCaseId,
                familyId,
                reportingParty,
                reportedByUserId,
                reportedOnUtc,
                visitedOnUtc,
                isSafetyConcern,
                description);

            dbContext.ComplaintReviews.Add(review);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<ComplaintReviewSnapshot>.Success(Map(review));
        }
        catch (DomainException ex)
        {
            return Result<ComplaintReviewSnapshot>.Failure(new Error(ComplaintReviewErrors.Invalid.Code, ex.Message));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A concurrent open for the same refund case lost the unique index
            // on refund_case_id; the case already has its review.
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.Unknown);
        }
    }

    public Task<Result<ComplaintReviewSnapshot>> BeginInvestigationAsync(
        Guid complaintReviewId,
        Guid investigatorUserId,
        DateTime startedOnUtc,
        string? evidenceReference,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            complaintReviewId,
            review => review.BeginInvestigation(investigatorUserId, startedOnUtc, evidenceReference),
            cancellationToken);

    /// <summary>
    /// Approves the reviewed award (#12) and appends the resulting movement in
    /// the same unit of work (#16: approval alone is not completion, so the
    /// verdict and its ledger entry are never separated). The movement is a pure
    /// MOVEMENT record (#35) appended through the frozen
    /// <see cref="RefundLedger"/> under the complaint's refund case id; a zero
    /// award is valid (#31) and recorded as computed.
    /// <para>
    /// A refund case may carry at most one funded award: if its ledger already
    /// holds an <c>Approved</c>, <c>TransferConfirmed</c> or
    /// <c>CreditBalancePosted</c> entry the approval is refused as a conflict, so
    /// a repeated approval can never append a second unlimited movement.
    /// </para>
    /// <para>
    /// Complaint compensation is deliberately <b>not</b> correlated with a
    /// subscription capture, and it is <b>not</b> capped by a capture budget.
    /// Whether compensation outside a specific capture should be capped is an
    /// Owner decision that remains recorded and unimplemented here.
    /// </para>
    /// </summary>
    public async Task<Result<ComplaintReviewSnapshot>> ApproveAsync(
        Guid complaintReviewId,
        Guid approvingUserId,
        DateTime approvedOnUtc,
        long awardMinorUnits,
        string? decisionNote,
        string? evidenceReference,
        CancellationToken cancellationToken)
    {
        if (awardMinorUnits < 0)
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.AmountInvalid);

        try
        {
            ComplaintReview? review = await dbContext.ComplaintReviews
                .SingleOrDefaultAsync(x => x.Id == complaintReviewId, cancellationToken);
            if (review is null)
                return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.NotFound);

            RefundLedger? ledger = await dbContext.RefundLedgers
                .Include(x => x.Entries)
                .SingleOrDefaultAsync(x => x.RefundCaseId == review.RefundCaseId, cancellationToken);

            // One funded award per refund case. A complaint case is joined to its
            // money by the case id alone, so without this guard a second approval —
            // or an approval on a case whose ledger already carries a funded
            // award from another channel — would append a second, unlimited
            // movement. Refused as a conflict rather than trimmed: the verdict and
            // the movement stay one atomic decision.
            if (ledger is not null && ledger.Entries.Any(entry => IsFundedAward(entry.EntryType)))
                return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.Conflict);

            review.Approve(approvingUserId, approvedOnUtc, decisionNote);

            if (ledger is null)
            {
                ledger = RefundLedger.Open(review.RefundCaseId);
                dbContext.RefundLedgers.Add(ledger);
            }

            ledger.Approve(
                Money.FromMinorUnits(awardMinorUnits),
                approvedOnUtc,
                approvingUserId,
                Normalize(evidenceReference));

            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<ComplaintReviewSnapshot>.Success(Map(review));
        }
        catch (DomainException ex)
        {
            return Result<ComplaintReviewSnapshot>.Failure(new Error(ComplaintReviewErrors.Invalid.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.Unknown);
        }
    }

    /// <summary>
    /// Whether a recorded movement already funds this case. Approval (#12) is a
    /// one-shot verdict and the completion states are the same money finally
    /// moving, so any of them means the case's compensation has already been
    /// awarded and a second approval would double-pay it.
    /// </summary>
    private static bool IsFundedAward(RefundLedgerEntryType entryType) =>
        entryType is RefundLedgerEntryType.Approved
            or RefundLedgerEntryType.TransferConfirmed
            or RefundLedgerEntryType.CreditBalancePosted;

    public Task<Result<ComplaintReviewSnapshot>> RefuseAsync(
        Guid complaintReviewId,
        Guid refusingUserId,
        DateTime refusedOnUtc,
        string? decisionNote,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            complaintReviewId,
            review => review.Refuse(refusingUserId, refusedOnUtc, decisionNote),
            cancellationToken);

    private async Task<Result<ComplaintReviewSnapshot>> TransitionAsync(
        Guid complaintReviewId,
        Action<ComplaintReview> transition,
        CancellationToken cancellationToken)
    {
        try
        {
            ComplaintReview? review = await dbContext.ComplaintReviews
                .SingleOrDefaultAsync(x => x.Id == complaintReviewId, cancellationToken);
            if (review is null)
                return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.NotFound);

            transition(review);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<ComplaintReviewSnapshot>.Success(Map(review));
        }
        catch (DomainException ex)
        {
            return Result<ComplaintReviewSnapshot>.Failure(new Error(ComplaintReviewErrors.Invalid.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<ComplaintReviewSnapshot>.Failure(ComplaintReviewErrors.Unknown);
        }
    }

    /// <summary>
    /// Trims an optional evidence reference and collapses blank input to null, so
    /// the ledger stores either a real reference or nothing — the same normalizing
    /// the sibling refund recorder applies.
    /// </summary>
    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }

    /// <summary>
    /// Identifies a unique/index violation across providers without masking
    /// anything else, matching the house precedent. Any other database failure
    /// is never caught here.
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

    private static ComplaintReviewSnapshot Map(ComplaintReview review) =>
        new(
            review.Id,
            review.RefundCaseId,
            review.FamilyId,
            review.ReportingParty,
            review.ReportedByUserId,
            review.ReportedOnUtc,
            review.VisitedOnUtc,
            review.Timeliness,
            review.State,
            review.Timeliness == ComplaintTimeliness.Late,
            review.Description,
            review.InvestigatorUserId,
            review.InvestigationStartedOnUtc,
            review.EvidenceReference,
            review.DecisionNote,
            review.ApprovedByUserId,
            review.ApprovedOnUtc,
            review.RefusedByUserId,
            review.RefusedOnUtc,
            review.ProcessingDeadlineStartsOnUtc,
            review.CreatedOnUtc,
            review.UpdatedOnUtc);
}
