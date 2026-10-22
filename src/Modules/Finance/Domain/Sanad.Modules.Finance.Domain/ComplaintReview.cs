using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Finance.Domain;

/// <summary>
/// Who reported the quality complaint. The reporting party is mandatory
/// evidence (Owner decision #14: "record timestamps, reporting party, evidence
/// and staff dispute decision").
/// </summary>
public enum ComplaintReportingParty
{
    /// <summary>The family account that received the service.</summary>
    Family = 1,

    /// <summary>The caregiver who delivered the service.</summary>
    Caregiver = 2,

    /// <summary>A care-home facility.</summary>
    CareHomeFacility = 3,

    /// <summary>The platform's own staff, opening the case on a customer's behalf.</summary>
    Staff = 4
}

/// <summary>
/// The review phase a complaint case is in.
/// <para>
/// This is the "investigating" phase the verification pack treats as an
/// observable state: SupportAdmin receives and investigates, SuperAdmin
/// approves reviewed financial awards (Owner decision #12). It is a stored
/// phase on the case rather than a ledger entry because approval is a money
/// movement recorded in the append-only <see cref="RefundLedger"/>, while the
/// review itself is staff work with no money attached.
/// </para>
/// </summary>
public enum ComplaintReviewState
{
    /// <summary>Received but not yet picked up by a reviewer.</summary>
    Received = 1,

    /// <summary>A SupportAdmin is investigating; no award may be approved yet (#12).</summary>
    Investigating = 2,

    /// <summary>SuperAdmin approved a reviewed award; the money movement is on the ledger (#12).</summary>
    Resolved = 3,

    /// <summary>The reviewed award was refused; no money moves (#11).</summary>
    Refused = 4
}

/// <summary>
/// Whether the complaint was submitted inside the 48-hour reporting window.
/// Owner decision #13: a late complaint is still <em>accepted</em>, but flagged
/// for late review with no automatic compensation promise. A safety report is
/// never gated by the window.
/// </summary>
public enum ComplaintTimeliness
{
    /// <summary>Reported within 48 hours of the visit (clause 5).</summary>
    WithinWindow = 1,

    /// <summary>Reported after 48 hours; accepted and flagged for late review (#13).</summary>
    Late = 2,

    /// <summary>A safety concern: reported immediately, outside the timing rule entirely (clause 5).</summary>
    Safety = 3
}

/// <summary>
/// The reviewed-complaint case aggregate (clause 5, Owner decisions #11-#14).
/// <para>
/// A quality complaint is a <em>reviewed outcome</em>, never an automatic full
/// refund: this aggregate records the investigation and its verdict, and the
/// money movement an approved verdict produces is written to the append-only
/// <see cref="RefundLedger"/> under this case's <see cref="RefundCaseId"/>
/// correlation key. The two live in the same Finance schema (Owner decision /
/// W3 design pin item 1), the same unit of work.
/// </para>
/// <para>
/// Investigation is attributable and append-only in spirit: the aggregate
/// exposes no way to erase who investigated, when, or what evidence was
/// considered, and a case cannot reach <see cref="ComplaintReviewState.Resolved"/>
/// without the SuperAdmin approval instant and actor that decision #12 requires.
/// </para>
/// </summary>
public sealed class ComplaintReview : AggregateRoot<Guid>
{
    public const int MaximumEvidenceReferenceLength = 300;
    public const int MaximumNoteLength = 2000;

    /// <summary>The clause-5 reporting window for a quality complaint.</summary>
    public static readonly TimeSpan ReportingWindow = TimeSpan.FromHours(48);

    private ComplaintReview() { }

    private ComplaintReview(
        Guid id,
        Guid refundCaseId,
        Guid familyId,
        ComplaintReportingParty reportingParty,
        Guid reportedByUserId,
        DateTime reportedOnUtc,
        DateTime? visitedOnUtc,
        ComplaintTimeliness timeliness) : base(id)
    {
        RefundCaseId = refundCaseId;
        FamilyId = familyId;
        ReportingParty = reportingParty;
        ReportedByUserId = reportedByUserId;
        ReportedOnUtc = reportedOnUtc;
        VisitedOnUtc = visitedOnUtc;
        Timeliness = timeliness;
        State = ComplaintReviewState.Received;
        CreatedOnUtc = reportedOnUtc;
        UpdatedOnUtc = reportedOnUtc;
    }

    /// <summary>
    /// Correlation key shared with this case's <see cref="RefundLedger"/>. A
    /// reviewed award is recorded as ledger entries under this id, so the
    /// reviewed complaint and its money movement are never separated.
    /// </summary>
    public Guid RefundCaseId { get; private set; }

    /// <summary>The family account whose service the complaint is about.</summary>
    public Guid FamilyId { get; private set; }

    public ComplaintReportingParty ReportingParty { get; private set; }

    /// <summary>Who reported it. Mandatory: an unattributable complaint is not evidence (#14).</summary>
    public Guid ReportedByUserId { get; private set; }

    public DateTime ReportedOnUtc { get; private set; }

    /// <summary>When the service was delivered, when the reporter supplied it.</summary>
    public DateTime? VisitedOnUtc { get; private set; }

    /// <summary>Inside / late / safety. Late complaints stay reviewable (#13).</summary>
    public ComplaintTimeliness Timeliness { get; private set; }

    public ComplaintReviewState State { get; private set; }

    /// <summary>Free-text description supplied by the reporting party.</summary>
    public string? Description { get; private set; }

    /// <summary>The SupportAdmin who investigated (Owner decision #12).</summary>
    public Guid? InvestigatorUserId { get; private set; }

    public DateTime? InvestigationStartedOnUtc { get; private set; }

    /// <summary>Reference to stored evidence considered during the investigation (#14).</summary>
    public string? EvidenceReference { get; private set; }

    /// <summary>The staff dispute decision recorded for this complaint (#14).</summary>
    public string? DecisionNote { get; private set; }

    /// <summary>
    /// The SuperAdmin who approved the reviewed award (Owner decision #12: only
    /// SuperAdmin approves reviewed financial awards).
    /// </summary>
    public Guid? ApprovedByUserId { get; private set; }

    public DateTime? ApprovedOnUtc { get; private set; }

    public Guid? RefusedByUserId { get; private set; }

    public DateTime? RefusedOnUtc { get; private set; }

    /// <summary>Decision #2's seven-business-day processing clock starts here, and only here.</summary>
    public DateTime? ProcessingDeadlineStartsOnUtc => ApprovedOnUtc;

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    /// <summary>
    /// True once a reviewer has picked the case up. Approval is refused before
    /// this, because SupportAdmin investigates first (#12).
    /// </summary>
    public bool IsUnderInvestigation => InvestigatorUserId is not null;

    /// <summary>
    /// Opens a complaint case. The reporting party and reporter are mandatory
    /// evidence (#14). The visit instant determines the 48-hour window when it
    /// is supplied; a safety report is exempt from the window (#13, clause 5).
    /// </summary>
    public static ComplaintReview Open(
        Guid refundCaseId,
        Guid familyId,
        ComplaintReportingParty reportingParty,
        Guid reportedByUserId,
        DateTime reportedOnUtc,
        DateTime? visitedOnUtc = null,
        bool isSafetyConcern = false,
        string? description = null)
    {
        if (refundCaseId == Guid.Empty)
            throw new DomainException("A complaint review requires a refund case.");
        if (familyId == Guid.Empty)
            throw new DomainException("A complaint review requires the family account it concerns.");
        if (!Enum.IsDefined(reportingParty))
            throw new DomainException("A complaint review requires a known reporting party.");
        if (reportedByUserId == Guid.Empty)
            throw new DomainException("A complaint review requires the user who reported it.");
        EnsureUtc(reportedOnUtc, nameof(reportedOnUtc));
        if (visitedOnUtc is not null)
            EnsureUtc(visitedOnUtc.Value, nameof(visitedOnUtc));

        if (visitedOnUtc is not null && reportedOnUtc < visitedOnUtc.Value)
            throw new DomainException("A complaint cannot be reported before the visit it concerns.");

        ComplaintTimeliness timeliness = isSafetyConcern
            ? ComplaintTimeliness.Safety
            : visitedOnUtc is not null && reportedOnUtc > visitedOnUtc.Value.Add(ReportingWindow)
                ? ComplaintTimeliness.Late
                : ComplaintTimeliness.WithinWindow;

        return new ComplaintReview(
            Guid.CreateVersion7(),
            refundCaseId,
            familyId,
            reportingParty,
            reportedByUserId,
            reportedOnUtc,
            visitedOnUtc,
            timeliness)
        {
            Description = Normalize(description, MaximumNoteLength, nameof(description))
        };
    }

    /// <summary>
    /// SupportAdmin picks the case up and starts the investigation (Owner
    /// decision #12). Records who investigated and when; the case must be
    /// received first.
    /// </summary>
    public void BeginInvestigation(Guid investigatorUserId, DateTime startedOnUtc, string? evidenceReference = null)
    {
        if (State != ComplaintReviewState.Received)
            throw new DomainException("Only a received complaint can enter investigation.");
        if (investigatorUserId == Guid.Empty)
            throw new DomainException("An investigation requires the reviewing administrator.");
        EnsureUtc(startedOnUtc, nameof(startedOnUtc));
        if (startedOnUtc < ReportedOnUtc)
            throw new DomainException("An investigation cannot start before the complaint was reported.");

        InvestigatorUserId = investigatorUserId;
        InvestigationStartedOnUtc = startedOnUtc;
        EvidenceReference = Normalize(evidenceReference, MaximumEvidenceReferenceLength, nameof(evidenceReference));
        State = ComplaintReviewState.Investigating;
        UpdatedOnUtc = startedOnUtc;
    }

    /// <summary>
    /// Attaches further evidence or the staff dispute decision while the case
    /// is under investigation (#14).
    /// </summary>
    public void RecordInvestigationEvidence(string? evidenceReference, string? decisionNote, DateTime recordedOnUtc)
    {
        if (State != ComplaintReviewState.Investigating)
            throw new DomainException("Investigation evidence can only be recorded while a complaint is under investigation.");
        EnsureUtc(recordedOnUtc, nameof(recordedOnUtc));

        if (evidenceReference is not null)
            EvidenceReference = Normalize(evidenceReference, MaximumEvidenceReferenceLength, nameof(evidenceReference));
        if (decisionNote is not null)
            DecisionNote = Normalize(decisionNote, MaximumNoteLength, nameof(decisionNote));
        UpdatedOnUtc = recordedOnUtc;
    }

    /// <summary>
    /// SuperAdmin approves the reviewed award (Owner decision #12). The money
    /// movement itself is appended to the case's <see cref="RefundLedger"/>
    /// under <see cref="RefundCaseId"/>; this call records the approval
    /// decision and starts the seven-business-day processing clock (#2).
    /// </summary>
    public void Approve(Guid approvingUserId, DateTime approvedOnUtc, string? decisionNote = null)
    {
        if (State != ComplaintReviewState.Investigating)
            throw new DomainException("Only a complaint under investigation can be approved.");
        if (approvingUserId == Guid.Empty)
            throw new DomainException("An approval requires the approving administrator.");
        EnsureUtc(approvedOnUtc, nameof(approvedOnUtc));
        if (InvestigationStartedOnUtc is not null && approvedOnUtc < InvestigationStartedOnUtc.Value)
            throw new DomainException("A complaint cannot be approved before its investigation started.");

        ApprovedByUserId = approvingUserId;
        ApprovedOnUtc = approvedOnUtc;
        if (decisionNote is not null)
            DecisionNote = Normalize(decisionNote, MaximumNoteLength, nameof(decisionNote));
        State = ComplaintReviewState.Resolved;
        UpdatedOnUtc = approvedOnUtc;
    }

    /// <summary>
    /// The reviewed award was refused (Owner decision #11: quality complaints
    /// are reviewed outcomes, not automatic refunds). No money moves, so no
    /// ledger entry is appended.
    /// </summary>
    public void Refuse(Guid refusingUserId, DateTime refusedOnUtc, string? decisionNote = null)
    {
        if (State != ComplaintReviewState.Investigating)
            throw new DomainException("Only a complaint under investigation can be refused.");
        if (refusingUserId == Guid.Empty)
            throw new DomainException("A refusal requires the deciding administrator.");
        EnsureUtc(refusedOnUtc, nameof(refusedOnUtc));

        RefusedByUserId = refusingUserId;
        RefusedOnUtc = refusedOnUtc;
        if (decisionNote is not null)
            DecisionNote = Normalize(decisionNote, MaximumNoteLength, nameof(decisionNote));
        State = ComplaintReviewState.Refused;
        UpdatedOnUtc = refusedOnUtc;
    }

    private static void EnsureUtc(DateTime value, string field)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new DomainException($"A complaint review {field} must be UTC.");
    }

    private static string? Normalize(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new DomainException($"Complaint review {field} must not exceed {maximumLength} characters.");

        return normalized;
    }
}
