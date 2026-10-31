using System.Reflection;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.Modules.Finance.Domain;

namespace Ahlena.UnitTests.Finance;

/// <summary>
/// W2-F0 money core: the append-only refund ledger and the reversal/transfer
/// evidence rules behind Owner decisions #12, #16, #17 and #35. Run green at
/// the W2 integration gate by the Mastermind.
/// </summary>
public sealed class RefundLedgerTests
{
    private static readonly Guid CaseId = Guid.Parse("0198e2c2-5555-7777-8888-000000000101");
    private static readonly Guid ActorId = Guid.Parse("0198e2c2-5555-7777-8888-0000000001a1");

    [Fact]
    public void Open_requires_a_refund_case()
    {
        Assert.Throws<DomainException>(() => RefundLedger.Open(Guid.Empty));
        Assert.Equal(CaseId, RefundLedger.Open(CaseId).RefundCaseId);
    }

    [Fact]
    public void Every_transition_appends_and_never_rewrites_prior_entries()
    {
        RefundLedger ledger = RefundLedger.Open(CaseId);
        Money award = Money.FromMajorUnits(120.50m);

        ledger.Request(award, Utc(1), null);
        ledger.Approve(award, Utc(2), ActorId, "complaint-42");
        ledger.InitiateTransfer(award, Utc(3), ActorId, "bank-destination");
        ledger.ConfirmTransfer(award, Utc(4), ActorId, "TRX-0001", "receipt");

        Assert.Equal(4, ledger.Entries.Count);
        Assert.Equal(
            [
                RefundLedgerEntryType.Requested,
                RefundLedgerEntryType.Approved,
                RefundLedgerEntryType.TransferInitiated,
                RefundLedgerEntryType.TransferConfirmed
            ],
            ledger.Entries.Select(entry => entry.EntryType));
        Assert.Equal(RefundLedgerEntryType.TransferConfirmed, ledger.LatestEntry!.EntryType);
    }

    [Fact]
    public void Entries_are_attributable_and_ordered_by_occurrence()
    {
        RefundLedger ledger = RefundLedger.Open(CaseId);
        Money award = Money.FromMajorUnits(50m);

        ledger.Request(award, Utc(1), ActorId, "customer request");
        ledger.Approve(award, Utc(2), ActorId);

        RefundLedgerEntry request = ledger.Entries.First();
        Assert.Equal(CaseId, request.RefundCaseId);
        Assert.Equal(ActorId, request.ActorUserId);
        Assert.Equal("customer request", request.EvidenceReference);
        Assert.Equal(Utc(1), request.OccurredOnUtc);
        Assert.Equal(5000L, request.Amount.MinorUnits);
    }

    [Fact]
    public void A_system_transition_may_have_no_actor_but_never_a_sentinel_actor()
    {
        RefundLedger ledger = RefundLedger.Open(CaseId);

        // Null is the documented "system acted" value (e.g. timeout sweep).
        ledger.Timeout(Money.FromMajorUnits(10m), Utc(1), null, "provider-ambiguous");

        // Guid.Empty is rejected rather than silently treated as system.
        RefundLedger other = RefundLedger.Open(CaseId);
        Assert.Throws<DomainException>(() =>
            other.Fail(Money.FromMajorUnits(10m), Utc(1), Guid.Empty));
        Assert.Empty(other.Entries);
    }

    [Fact]
    public void Timestamps_must_be_utc_and_do_not_mutate_on_rejection()
    {
        RefundLedger ledger = RefundLedger.Open(CaseId);

        Assert.Throws<DomainException>(() => ledger.Request(
            Money.FromMajorUnits(10m),
            new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Local),
            ActorId));

        Assert.Empty(ledger.Entries);
    }

    [Fact]
    public void Transfer_confirmation_requires_a_provider_reference()
    {
        RefundLedger ledger = OpenApprovedCase(Money.FromMajorUnits(75m));

        Assert.Throws<DomainException>(() => ledger.ConfirmTransfer(
            Money.FromMajorUnits(75m),
            Utc(3),
            ActorId,
            "   ",
            "receipt"));

        Assert.Equal(RefundLedgerEntryType.Approved, ledger.LatestEntry!.EntryType);
    }

    [Fact]
    public void Approval_alone_is_not_completion()
    {
        RefundLedger ledger = OpenApprovedCase(Money.FromMajorUnits(75m));

        // Only the approval has been recorded: nothing has actually been paid.
        Assert.Equal(RefundLedgerEntryType.Approved, ledger.LatestEntry!.EntryType);
        Assert.Equal(0L, ledger.TotalCompleted().MinorUnits);
        Assert.Equal(7500L, ledger.NetRecorded().MinorUnits);
    }

    [Fact]
    public void Confirmed_transfers_and_posted_credit_count_as_completed_money()
    {
        RefundLedger ledger = OpenApprovedCase(Money.FromMajorUnits(100m));
        ledger.InitiateTransfer(Money.FromMajorUnits(40m), Utc(3), ActorId, "bank");
        ledger.ConfirmTransfer(Money.FromMajorUnits(40m), Utc(4), ActorId, "TRX-1", "receipt");
        ledger.PostCredit(Money.FromMajorUnits(60m), Utc(5), ActorId, "customer chose credit");

        Assert.Equal(10000L, ledger.TotalCompleted().MinorUnits);
        Assert.Equal(2, ledger.Entries.Count(entry =>
            entry.EntryType is RefundLedgerEntryType.TransferConfirmed or RefundLedgerEntryType.CreditBalancePosted));
    }

    [Fact]
    public void An_unresolved_timeout_blocks_everything_but_reconciliation()
    {
        RefundLedger ledger = OpenApprovedCase(Money.FromMajorUnits(200m));

        ledger.Timeout(Money.FromMajorUnits(200m), Utc(3), null, "gateway-timeout");
        Assert.True(ledger.HasUnresolvedTimeout);

        // Owner decision #17: while the ambiguity stands, NO retry path is
        // open — not a confirmation, not a new transfer, not credit, not a
        // failure record. Only ReconcileTimeout may follow.
        Assert.Throws<DomainException>(() => ledger.ConfirmTransfer(
            Money.FromMajorUnits(200m), Utc(4), ActorId, "TRX-2", null));
        Assert.Throws<DomainException>(() => ledger.InitiateTransfer(
            Money.FromMajorUnits(200m), Utc(4), ActorId, "bank"));
        Assert.Throws<DomainException>(() => ledger.PostCredit(
            Money.FromMajorUnits(200m), Utc(4), ActorId));
        Assert.Throws<DomainException>(() => ledger.Fail(
            Money.FromMajorUnits(200m), Utc(4), ActorId, "try"));

        Assert.True(ledger.HasUnresolvedTimeout);
        Assert.Equal(3, ledger.Entries.Count);
    }

    [Fact]
    public void Timeout_reconciliation_carries_evidence_and_clears_the_block()
    {
        RefundLedger ledger = OpenApprovedCase(Money.FromMajorUnits(200m));
        ledger.Timeout(Money.FromMajorUnits(200m), Utc(3), null, "gateway-timeout");

        ledger.ReconcileTimeout(Utc(4), ActorId, "gateway reconciliation report", "TRX-2");

        Assert.False(ledger.HasUnresolvedTimeout);
        RefundLedgerEntry reconciliation = ledger.LatestEntry!;
        Assert.Equal("gateway reconciliation report", reconciliation.EvidenceReference);
        Assert.Equal("TRX-2", reconciliation.ProviderReference);
        // The reconciliation itself is not a money movement.
        Assert.Equal(0L, reconciliation.Amount.MinorUnits);

        // After reconciliation the case can be completed.
        ledger.ConfirmTransfer(Money.FromMajorUnits(200m), Utc(5), ActorId, "TRX-2", "receipt");
    }

    [Fact]
    public void Reconciliation_without_a_timeout_or_without_evidence_is_refused()
    {
        RefundLedger ledger = OpenApprovedCase(Money.FromMajorUnits(10m));

        Assert.Throws<DomainException>(() => ledger.ReconcileTimeout(Utc(3), ActorId, "report"));

        ledger.Timeout(Money.FromMajorUnits(10m), Utc(4), null, "ambiguous");
        Assert.Throws<DomainException>(() => ledger.ReconcileTimeout(Utc(5), ActorId, "  "));
        Assert.True(ledger.HasUnresolvedTimeout);
    }

    [Fact]
    public void Transfer_initiation_requires_the_customer_destination_evidence()
    {
        RefundLedger ledger = OpenApprovedCase(Money.FromMajorUnits(75m));

        // Owner decision #16: the customer's selected destination is part of
        // the evidence chain and cannot be added retroactively.
        Assert.Throws<DomainException>(() => ledger.InitiateTransfer(
            Money.FromMajorUnits(75m), Utc(3), ActorId, null));
        Assert.Throws<DomainException>(() => ledger.InitiateTransfer(
            Money.FromMajorUnits(75m), Utc(3), ActorId, "   "));

        Assert.Equal(RefundLedgerEntryType.Approved, ledger.LatestEntry!.EntryType);
    }

    [Fact]
    public void Award_bearing_transitions_refuse_negative_amounts_and_fail_carries_the_signed_reversal()
    {
        RefundLedger ledger = OpenApprovedCase(Money.FromMajorUnits(100m));
        Money negative = Money.FromMinorUnits(-1);

        Assert.Throws<DomainException>(() => ledger.Approve(negative, Utc(3), ActorId));
        Assert.Throws<DomainException>(() => ledger.InitiateTransfer(negative, Utc(3), ActorId, "bank"));
        Assert.Throws<DomainException>(() => ledger.ConfirmTransfer(negative, Utc(3), ActorId, "TRX", null));
        Assert.Throws<DomainException>(() => ledger.PostCredit(negative, Utc(3), ActorId));
        Assert.Throws<DomainException>(() => ledger.Timeout(negative, Utc(3), null, "ambiguous"));

        // Fail is the documented signed-reversal carrier; zero-award rules (#31)
        // keep zero valid everywhere.
        ledger.Approve(Money.Zero, Utc(3), ActorId);
        ledger.Fail(negative, Utc(4), ActorId, "reversal");
        Assert.Equal(-1L, ledger.LatestEntry!.Amount.MinorUnits);
    }

    [Fact]
    public void Rejection_records_no_money_movement()
    {
        RefundLedger ledger = RefundLedger.Open(CaseId);
        ledger.Request(Money.FromMajorUnits(30m), Utc(1), ActorId);

        ledger.Reject(Utc(2), ActorId, "outside policy window");

        Assert.Equal(RefundLedgerEntryType.Rejected, ledger.LatestEntry!.EntryType);
        Assert.Equal(0L, ledger.LatestEntry.Amount.MinorUnits);
        Assert.Equal(0L, ledger.TotalCompleted().MinorUnits);
    }

    [Fact]
    public void A_zero_piastre_award_is_a_valid_recorded_amount()
    {
        // Owner decision #31: an exact time-proration may award 0 piastres.
        RefundLedger ledger = RefundLedger.Open(CaseId);
        Money zero = Money.FromMinorUnits(0);

        ledger.Request(zero, Utc(1), ActorId);
        ledger.Approve(zero, Utc(2), ActorId);

        Assert.Equal(0L, ledger.LatestEntry!.Amount.MinorUnits);
        Assert.True(ledger.LatestEntry.Amount.IsZero);
    }

    [Fact]
    public void Net_recorded_sums_signed_amounts()
    {
        RefundLedger ledger = OpenApprovedCase(Money.FromMajorUnits(100m));
        ledger.PostCredit(Money.FromMajorUnits(100m), Utc(3), ActorId);
        ledger.Fail(Money.FromMajorUnits(-25m), Utc(4), ActorId, "reversal");

        // 100 (approve) + 100 (credit) + (-25) reversal = 175.00 EGP.
        Assert.Equal(17500L, ledger.NetRecorded().MinorUnits);
    }

    [Fact]
    public void Evidence_and_provider_references_are_bounded_and_trimmed()
    {
        RefundLedger ledger = RefundLedger.Open(CaseId);

        ledger.Request(Money.FromMajorUnits(1m), Utc(1), ActorId, "  spaced  ");
        Assert.Equal("spaced", ledger.LatestEntry!.EvidenceReference);

        Assert.Throws<DomainException>(() => ledger.Request(
            Money.FromMajorUnits(1m),
            Utc(2),
            ActorId,
            new string('x', RefundLedgerEntry.MaximumEvidenceReferenceLength + 1)));

        RefundLedger other = OpenApprovedCase(Money.FromMajorUnits(1m));
        Assert.Throws<DomainException>(() => other.ConfirmTransfer(
            Money.FromMajorUnits(1m),
            Utc(3),
            ActorId,
            new string('y', RefundLedgerEntry.MaximumProviderReferenceLength + 1),
            null));
    }

    [Fact]
    public void Blank_evidence_and_provider_references_normalize_to_null()
    {
        RefundLedger ledger = RefundLedger.Open(CaseId);

        ledger.Request(Money.FromMajorUnits(1m), Utc(1), ActorId, "   ");
        Assert.Null(ledger.LatestEntry!.EvidenceReference);
        Assert.Null(ledger.LatestEntry.ProviderReference);
    }

    [Fact]
    public void Entries_are_append_only_and_expose_no_public_mutator()
    {
        Type type = typeof(RefundLedgerEntry);

        Assert.True(type.IsSealed);
        Assert.All(
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
            property => Assert.Null(property.GetSetMethod(nonPublic: false)));
        Assert.DoesNotContain(
            type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(method => method.Name),
            name => name is "Apply" or "Update" or "Set" or "Delete" or "Remove");
    }

    [Fact]
    public void Unknown_entry_types_are_rejected()
    {
        RefundLedger ledger = RefundLedger.Open(CaseId);

        // The public surface only exposes real transitions, so an undefined
        // enum value can only arrive through creation; assert the guard holds.
        Assert.Throws<DomainException>(() => RefundLedgerEntry.Create(
            CaseId,
            (RefundLedgerEntryType)99,
            Money.FromMajorUnits(1m),
            Utc(1),
            ActorId,
            null,
            null));

        // The ledger itself exposes no way to smuggle one in.
        Assert.DoesNotContain(
            typeof(RefundLedger).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Select(method => method.Name),
            name => name.Equals("Append", StringComparison.Ordinal));
    }

    [Fact]
    public void Entry_requires_a_case_even_when_created_directly()
    {
        Assert.Throws<DomainException>(() => RefundLedgerEntry.Create(
            Guid.Empty,
            RefundLedgerEntryType.Requested,
            Money.FromMajorUnits(1m),
            Utc(1),
            ActorId,
            null,
            null));
    }

    private static RefundLedger OpenApprovedCase(Money award)
    {
        RefundLedger ledger = RefundLedger.Open(CaseId);
        ledger.Request(award, Utc(1), ActorId);
        ledger.Approve(award, Utc(2), ActorId, "investigation-evidence");
        return ledger;
    }

    private static DateTime Utc(int hour) => new(2026, 10, 22, hour, 0, 0, DateTimeKind.Utc);
}
