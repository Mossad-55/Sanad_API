using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;
using Sanad.UnitTests.Support;

namespace Sanad.UnitTests.Finance;

/// <summary>
/// W3-F3 complaint-review application seam over <see cref="IFinanceDbContext"/>.
/// The store maps the aggregate to the read model and enforces one review per
/// refund case; the transition handlers map domain guards to the stable
/// <c>Finance.Complaint.*</c> codes the API contract exposes.
/// <para>NOT RUN: authored for the W3 integration gate; the Mastermind runs it.</para>
/// </summary>
public sealed class ComplaintReviewStoreTests
{
    private static readonly Guid Case = Guid.Parse("0198e2c2-5555-7777-8888-000000000301");
    private static readonly Guid Family = Guid.Parse("0198e2c2-5555-7777-8888-000000000302");
    private static readonly Guid Reporter = Guid.Parse("0198e2c2-5555-7777-8888-000000000303");
    private static readonly Guid Reviewer = Guid.Parse("0198e2c2-5555-7777-8888-000000000304");
    private static DateTime Utc(int hour) => new(2026, 10, 22, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Open_then_read_back_round_trips_the_projection()
    {
        await using var db = new Adapter(CreateContext());
        var store = new ComplaintReviewStore(db);

        var opened = await store.OpenAsync(
            Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9),
            Utc(8), isSafetyConcern: false, "late arrival", default);

        Assert.True(opened.IsSuccess);
        Assert.Equal(ComplaintReviewState.Received, opened.Value.ReviewState);
        Assert.Equal(ComplaintTimeliness.WithinWindow, opened.Value.Timeliness);

        var byId = await store.GetAsync(opened.Value.Id, default);
        var byCase = await store.FindForCaseAsync(Case, default);

        Assert.NotNull(byId);
        Assert.Equal(opened.Value.Id, byId!.Id);
        Assert.NotNull(byCase);
        Assert.Equal(opened.Value.Id, byCase!.Id);
    }

    [Fact]
    public async Task A_second_review_for_the_same_refund_case_is_a_conflict()
    {
        await using var db = new Adapter(CreateContext());
        var store = new ComplaintReviewStore(db);

        var first = await store.OpenAsync(Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9), null, false, null, default);
        var second = await store.OpenAsync(Case, Family, ComplaintReportingParty.Family, Reporter, Utc(10), null, false, null, default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsFailure);
        Assert.Equal("Finance.Complaint.Conflict", second.Error.Code);
    }

    [Fact]
    public async Task Transitions_map_domain_guards_to_the_invalid_code()
    {
        await using var db = new Adapter(CreateContext());
        var store = new ComplaintReviewStore(db);

        var opened = await store.OpenAsync(Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9), null, false, null, default);

        // Approving before an investigation starts is a domain guard.
        var tooEarly = await store.ApproveAsync(opened.Value.Id, Reviewer, Utc(10), 15000, null, null, default);
        Assert.True(tooEarly.IsFailure);
        Assert.Equal("Finance.Complaint.Invalid", tooEarly.Error.Code);

        var investigating = await store.BeginInvestigationAsync(opened.Value.Id, Reviewer, Utc(10), "ref", default);
        Assert.True(investigating.IsSuccess);
        Assert.Equal(ComplaintReviewState.Investigating, investigating.Value.ReviewState);

        var approved = await store.ApproveAsync(opened.Value.Id, Reviewer, Utc(11), 15000, "ok", "support-case-1", default);
        Assert.True(approved.IsSuccess);
        Assert.Equal(ComplaintReviewState.Resolved, approved.Value.ReviewState);
        Assert.Equal(Utc(11), approved.Value.ProcessingDeadlineStartsOnUtc);

        // M2: the approval appended exactly one Approved movement under the case.
        var ledger = await db.RefundLedgers
            .Include(x => x.Entries)
            .SingleAsync(x => x.RefundCaseId == Case, default);
        var entry = Assert.Single(ledger.Entries);
        Assert.Equal(RefundLedgerEntryType.Approved, entry.EntryType);
        Assert.Equal(15000, entry.Amount.MinorUnits);
    }

    [Fact]
    public async Task A_second_award_on_a_case_that_already_holds_a_funded_award_is_a_conflict()
    {
        // N2: complaint compensation is joined to its money by the refund case id
        // alone, so a second approval would append a second unlimited movement.
        // The ledger guard must refuse even though the review is still mid-flight
        // (the domain state machine alone would not catch an award recorded under
        // this case by another channel).
        await using var db = new Adapter(CreateContext());
        var store = new ComplaintReviewStore(db);

        var opened = await store.OpenAsync(Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9), null, false, null, default);
        await store.BeginInvestigationAsync(opened.Value.Id, Reviewer, Utc(10), null, default);

        RefundLedger ledger = RefundLedger.Open(Case);
        ledger.Approve(Money.FromMinorUnits(5000), Utc(10), Reviewer, "external-award");
        db.RefundLedgers.Add(ledger);
        await db.SaveChangesAsync();

        var refused = await store.ApproveAsync(opened.Value.Id, Reviewer, Utc(11), 15000, null, null, default);

        Assert.True(refused.IsFailure);
        Assert.Equal("Finance.Complaint.Conflict", refused.Error.Code);

        // Exactly the original award remains: no second movement was appended.
        RefundLedger stored = await db.RefundLedgers
            .Include(x => x.Entries)
            .SingleAsync(x => x.RefundCaseId == Case, default);
        RefundLedgerEntry entry = Assert.Single(stored.Entries);
        Assert.Equal(5000, entry.Amount.MinorUnits);

        // And the refused approval left no half-verdict behind.
        var stillOpen = await store.GetAsync(opened.Value.Id, default);
        Assert.Equal(ComplaintReviewState.Investigating, stillOpen!.ReviewState);
    }

    [Fact]
    public async Task A_negative_award_is_rejected_and_appends_no_movement()
    {
        await using var db = new Adapter(CreateContext());
        var store = new ComplaintReviewStore(db);

        var opened = await store.OpenAsync(Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9), null, false, null, default);
        await store.BeginInvestigationAsync(opened.Value.Id, Reviewer, Utc(10), null, default);

        var bad = await store.ApproveAsync(opened.Value.Id, Reviewer, Utc(11), -1, null, null, default);

        Assert.True(bad.IsFailure);
        Assert.Equal("Finance.Complaint.AmountInvalid", bad.Error.Code);

        // The rejected verdict rolled back with the movement: no ledger row, and
        // the case is still under investigation (never half-approved).
        Assert.False(await db.RefundLedgers.AnyAsync(x => x.RefundCaseId == Case, default));
        var stillOpen = await store.GetAsync(opened.Value.Id, default);
        Assert.Equal(ComplaintReviewState.Investigating, stillOpen!.ReviewState);
    }

    [Fact]
    public async Task A_missing_review_maps_to_not_found()
    {
        await using var db = new Adapter(CreateContext());
        var store = new ComplaintReviewStore(db);

        var result = await store.ApproveAsync(Guid.NewGuid(), Reviewer, Utc(10), 0, null, null, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Finance.Complaint.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task List_filters_by_state_and_family()
    {
        await using var db = new Adapter(CreateContext());
        var store = new ComplaintReviewStore(db);
        Guid familyTwo = Guid.NewGuid();

        var one = await store.OpenAsync(Case, Family, ComplaintReportingParty.Family, Reporter, Utc(9), null, false, null, default);
        await store.OpenAsync(Guid.NewGuid(), familyTwo, ComplaintReportingParty.Caregiver, Reporter, Utc(10), null, false, null, default);
        await store.BeginInvestigationAsync(one.Value.Id, Reviewer, Utc(10), null, default);

        var investigating = await store.ListAsync(ComplaintReviewState.Investigating, null, default);
        var byFamily = await store.ListAsync(null, Family, default);

        Assert.Single(investigating);
        Assert.Single(byFamily);
        Assert.Equal(one.Value.Id, byFamily[0].Id);
    }

    private static FinanceDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private sealed class Adapter(FinanceDbContext inner)
        : FinanceDbContextAdapter(inner), IAsyncDisposable
    {
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
