using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.UnitTests.Finance;

/// <summary>
/// W3-F3 reviewed-complaint workflow (clause 5, Owner decisions #11-#14). The
/// reviewed complaint is a <em>reviewed outcome</em>, never an automatic full
/// refund: SupportAdmin investigates (#12), a late complaint is still accepted
/// but flagged (#13), a safety concern is never gated by the window, and the
/// reporting party and timestamps are mandatory evidence (#14). Domain guards
/// throw <see cref="DomainException"/> (house convention).
/// <para>NOT RUN: authored for the W3 integration gate; the Mastermind runs it.</para>
/// </summary>
public sealed class ComplaintReviewTests
{
    private static readonly Guid Case = Guid.Parse("0198e2c2-3333-7777-8888-000000000101");
    private static readonly Guid Family = Guid.Parse("0198e2c2-3333-7777-8888-000000000102");
    private static readonly Guid Reporter = Guid.Parse("0198e2c2-3333-7777-8888-000000000103");
    private static readonly Guid Reviewer = Guid.Parse("0198e2c2-3333-7777-8888-000000000104");

    private static DateTime Utc(int hour) => new(2026, 10, 22, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Open_requires_case_family_reporter_and_a_known_reporting_party()
    {
        Assert.Throws<DomainException>(() => ComplaintReview.Open(
            Guid.Empty, Family, ComplaintReportingParty.Family, Reporter, Utc(9)));
        Assert.Throws<DomainException>(() => ComplaintReview.Open(
            Case, Guid.Empty, ComplaintReportingParty.Family, Reporter, Utc(9)));
        Assert.Throws<DomainException>(() => ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Guid.Empty, Utc(9)));
        Assert.Throws<DomainException>(() => ComplaintReview.Open(
            Case, Family, (ComplaintReportingParty)99, Reporter, Utc(9)));

        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9));

        Assert.Equal(ComplaintReviewState.Received, review.State);
        Assert.Equal(Reporter, review.ReportedByUserId);
        Assert.False(review.IsUnderInvestigation);
    }

    [Fact]
    public void Open_requires_utc_timestamps()
    {
        Assert.Throws<DomainException>(() => ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter,
            new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void A_complaint_cannot_be_reported_before_the_visit_it_concerns()
    {
        Assert.Throws<DomainException>(() => ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter, Utc(8), visitedOnUtc: Utc(9)));
    }

    [Fact]
    public void Within_window_complaint_is_not_flagged()
    {
        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter,
            reportedOnUtc: Utc(10), visitedOnUtc: Utc(9));

        Assert.Equal(ComplaintTimeliness.WithinWindow, review.Timeliness);
    }

    [Fact]
    public void Late_complaint_is_accepted_but_flagged_for_late_review()
    {
        // #13: reported after the 48-hour window is still accepted, with no
        // automatic compensation promise.
        DateTime reported = Utc(9).AddDays(3);
        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Caregiver, Reporter,
            reportedOnUtc: reported, visitedOnUtc: Utc(9));

        Assert.Equal(ComplaintReviewState.Received, review.State);
        Assert.Equal(ComplaintTimeliness.Late, review.Timeliness);
    }

    [Fact]
    public void A_complaint_reported_exactly_at_the_48_hour_boundary_is_still_within_window()
    {
        // #13: the window is inclusive at exactly 48 hours; only strictly later is
        // Late. This pins the boundary so the comparison cannot drift.
        DateTime visited = Utc(9);
        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter,
            reportedOnUtc: visited.AddHours(48), visitedOnUtc: visited);

        Assert.Equal(ComplaintTimeliness.WithinWindow, review.Timeliness);
    }

    [Fact]
    public void A_safety_concern_is_never_gated_by_the_window()
    {
        DateTime reported = Utc(9).AddDays(30);
        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.CareHomeFacility, Reporter,
            reportedOnUtc: reported, visitedOnUtc: Utc(9), isSafetyConcern: true);

        Assert.Equal(ComplaintTimeliness.Safety, review.Timeliness);
    }

    [Fact]
    public void Approval_is_refused_before_an_investigation_starts()
    {
        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9));

        Assert.Throws<DomainException>(() => review.Approve(Reviewer, Utc(10)));
    }

    [Fact]
    public void Investigate_records_the_reviewer_and_is_repeatable_only_once()
    {
        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9));

        review.BeginInvestigation(Reviewer, Utc(10), "support-case-1");

        Assert.Equal(ComplaintReviewState.Investigating, review.State);
        Assert.Equal(Reviewer, review.InvestigatorUserId);
        Assert.Equal(Utc(10), review.InvestigationStartedOnUtc);
        Assert.Equal("support-case-1", review.EvidenceReference);
        Assert.True(review.IsUnderInvestigation);
        Assert.Throws<DomainException>(() => review.BeginInvestigation(Reviewer, Utc(11)));
    }

    [Fact]
    public void Approve_resolves_and_starts_the_processing_clock()
    {
        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9));
        review.BeginInvestigation(Reviewer, Utc(10));

        review.Approve(Reviewer, Utc(11), "reviewed, partial award");

        Assert.Equal(ComplaintReviewState.Resolved, review.State);
        Assert.Equal(Reviewer, review.ApprovedByUserId);
        Assert.Equal(Utc(11), review.ApprovedOnUtc);
        Assert.Equal(Utc(11), review.ProcessingDeadlineStartsOnUtc);
        Assert.Equal("reviewed, partial award", review.DecisionNote);

        // A resolved case cannot be approved again.
        Assert.Throws<DomainException>(() => review.Approve(Reviewer, Utc(12)));
    }

    [Fact]
    public void Approve_cannot_precede_the_investigation_start()
    {
        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9));
        review.BeginInvestigation(Reviewer, Utc(10));

        Assert.Throws<DomainException>(() => review.Approve(Reviewer, Utc(9)));
    }

    [Fact]
    public void Refuse_moves_to_refused_without_money()
    {
        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9));
        review.BeginInvestigation(Reviewer, Utc(10));

        review.Refuse(Reviewer, Utc(11), "no award due");

        Assert.Equal(ComplaintReviewState.Refused, review.State);
        Assert.Equal(Reviewer, review.RefusedByUserId);
        Assert.Equal(Utc(11), review.RefusedOnUtc);
        Assert.Null(review.ApprovedOnUtc);
        Assert.Null(review.ProcessingDeadlineStartsOnUtc);
    }

    [Fact]
    public void Investigation_evidence_and_notes_are_bounded_and_trimmed()
    {
        ComplaintReview review = ComplaintReview.Open(
            Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9), description: "  late arrival  ");
        review.BeginInvestigation(Reviewer, Utc(10));

        review.RecordInvestigationEvidence("  evidence-ref  ", "  decision note  ", Utc(10));

        Assert.Equal("evidence-ref", review.EvidenceReference);
        Assert.Equal("decision note", review.DecisionNote);

        Assert.Throws<DomainException>(() => review.RecordInvestigationEvidence(
            new string('x', ComplaintReview.MaximumEvidenceReferenceLength + 1), null, Utc(10)));
    }
}
