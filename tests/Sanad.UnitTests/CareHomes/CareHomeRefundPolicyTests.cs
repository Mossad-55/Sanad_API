using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.UnitTests.CareHomes;

/// <summary>
/// Revision-2 care-home refund engine (W3-F2): the terms value object, the
/// clause-2 default schedule, the award arithmetic and its rounding/clamping,
/// the snapshot integrity rules and the dormancy switch. The allowed window
/// boundaries below follow the contract's financial checks (#4, clause 2/6,
/// decisions #7–#10).
/// </summary>
public sealed class CareHomeRefundPolicyTests
{
    private static readonly DateTime Start = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Cancellation_tier_ctor_rejects_percentages_and_deadlines_outside_the_supported_ranges()
    {
        Assert.Throws<DomainException>(() => new CareHomeCancellationTermTier(-1, 24));
        Assert.Throws<DomainException>(() => new CareHomeCancellationTermTier(101, 24));
        Assert.Throws<DomainException>(() => new CareHomeCancellationTermTier(100, -1));
        Assert.Throws<DomainException>(() => new CareHomeCancellationTermTier(100, 8761));

        // Range boundaries themselves are valid: a full 0–100 percentage span
        // and a deadline anywhere from 0 up to 8760 hours.
        _ = new CareHomeCancellationTermTier(0, CareHomeCancellationTermTier.MinimumDeadlineHours);
        _ = new CareHomeCancellationTermTier(100, CareHomeCancellationTermTier.MaximumDeadlineHours);
    }

    [Fact]
    public void Terms_normalize_tiers_longest_deadline_first_and_are_deterministic_roundtrip_json()
    {
        CareHomeCancellationTerms terms = CareHomeCancellationTerms.From(
        [
            new CareHomeCancellationTermTier(50, 12),
            new CareHomeCancellationTermTier(100, 48),
            new CareHomeCancellationTermTier(0, 0),
        ]);

        Assert.Equal(new CareHomeCancellationTermTier(100, 48), terms.Tiers[0]);
        Assert.Equal(new CareHomeCancellationTermTier(50, 12), terms.Tiers[1]);
        Assert.Equal(new CareHomeCancellationTermTier(0, 0), terms.Tiers[2]);

        string expected = """[{"refundPercentage":100,"deadlineHoursBeforeStart":48},{"refundPercentage":50,"deadlineHoursBeforeStart":12},{"refundPercentage":0,"deadlineHoursBeforeStart":0}]""";
        Assert.Equal(expected, terms.ToJson());
        Assert.Equal(expected, terms.ToJson());
        Assert.Equal(terms, CareHomeCancellationTerms.Parse(terms.ToJson()));
    }

    [Fact]
    public void Terms_reject_duplicate_deadlines_empty_or_missing_lists_and_more_than_six_tiers()
    {
        Assert.Throws<DomainException>(() => CareHomeCancellationTerms.From(
            [new CareHomeCancellationTermTier(100, 48), new CareHomeCancellationTermTier(50, 48)]));
        Assert.Throws<DomainException>(() => CareHomeCancellationTerms.From(null));
        Assert.Throws<DomainException>(() => CareHomeCancellationTerms.From([]));
        Assert.Throws<DomainException>(() => CareHomeCancellationTerms.From(
        [
            new CareHomeCancellationTermTier(100, 48), new CareHomeCancellationTermTier(90, 40),
            new CareHomeCancellationTermTier(80, 36), new CareHomeCancellationTermTier(70, 30),
            new CareHomeCancellationTermTier(60, 24), new CareHomeCancellationTermTier(40, 12),
            new CareHomeCancellationTermTier(20, 6),
        ]));
    }

    [Fact]
    public void Parse_returns_null_for_missing_terms_and_fails_loudly_on_unreadable_json()
    {
        Assert.Null(CareHomeCancellationTerms.Parse(null));
        Assert.Null(CareHomeCancellationTerms.Parse("   "));
        Assert.Throws<DomainException>(() => CareHomeCancellationTerms.Parse("{not-json"));
    }

    [Fact]
    public void ShareFor_resolves_the_first_satisfied_tier_with_exact_deadline_boundaries()
    {
        CareHomeCancellationTerms terms = CareHomeCancellationTerms.From(
        [
            new CareHomeCancellationTermTier(100, 48),
            new CareHomeCancellationTermTier(50, 12),
        ]);

        Assert.Equal(1m, terms.ShareFor(Start, Start - TimeSpan.FromHours(48)));
        Assert.Equal(0.5m, terms.ShareFor(Start, Start - TimeSpan.FromHours(48) + TimeSpan.FromMinutes(1)));
        Assert.Equal(0.5m, terms.ShareFor(Start, Start - TimeSpan.FromHours(12)));
        Assert.Equal(0m, terms.ShareFor(Start, Start - TimeSpan.FromHours(12) + TimeSpan.FromMinutes(1)));
        Assert.Equal(0m, terms.ShareFor(Start, Start));
        Assert.Equal(0m, terms.ShareFor(Start, Start + TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Cancel_terms_requests_and_share_resolution_reject_non_utc_instants()
    {
        CareHomeCancellationTerms terms = CareHomeCancellationTerms.From([new CareHomeCancellationTermTier(100, 48)]);
        DateTime local = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Local);

        Assert.Throws<DomainException>(() => terms.ShareFor(Start, local));
        Assert.Throws<DomainException>(() => terms.ShareFor(local, Start));
        Assert.Throws<DomainException>(() => CareHomeRefundPolicy.DefaultShareFor(Start, local));
    }

    [Fact]
    public void Default_schedule_refunds_full_at_or_before_24h_half_inside_and_zero_at_or_after_start()
    {
        Assert.Equal(1m, CareHomeRefundPolicy.DefaultShareFor(Start, Start - TimeSpan.FromHours(24)));
        Assert.Equal(0.5m, CareHomeRefundPolicy.DefaultShareFor(Start, Start - TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1)));
        Assert.Equal(0.5m, CareHomeRefundPolicy.DefaultShareFor(Start, Start - TimeSpan.FromMinutes(1)));
        Assert.Equal(0m, CareHomeRefundPolicy.DefaultShareFor(Start, Start));
        Assert.Equal(0m, CareHomeRefundPolicy.DefaultShareFor(Start, Start + TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Decide_rounds_awards_half_to_even_at_two_decimals()
    {
        DateTime scheduledStart = CareHomeStayStart.ScheduledStartUtc(new DateOnly(2026, 11, 1));

        // 100.05 * 0.5 = 50.025 → banker's rounding to 50.02 (the AwayFromZero
        // legacy rule would produce 50.03). Acting an hour inside the full
        // window exercises the 50% default share.
        var defaultScheduleDecision = CareHomeRefundPolicy.Decide(new CareHomeRefundPolicyInput(
            new DateOnly(2026, 11, 1), scheduledStart - TimeSpan.FromHours(23), 100.05m, null));
        Assert.Equal(50.02m, defaultScheduleDecision.AwardAmount);

        // 100.15 * 0.5 = 50.075 → 50.08 (the only even neighbor at the midpoint in cents).
        var second = CareHomeRefundPolicy.Decide(new CareHomeRefundPolicyInput(
            new DateOnly(2026, 11, 1), scheduledStart - TimeSpan.FromHours(20), 100.15m, null));
        Assert.Equal(50.08m, second.AwardAmount);
    }

    [Fact]
    public void Decide_zero_captured_reports_a_zero_award_and_the_naming_basis_stays_explicit()
    {
        CareHomeCancellationTerms terms = CareHomeCancellationTerms.From([new CareHomeCancellationTermTier(100, 48)]);
        DateTime scheduledStart = CareHomeStayStart.ScheduledStartUtc(new DateOnly(2026, 11, 1));

        var fromTerms = CareHomeRefundPolicy.Decide(new CareHomeRefundPolicyInput(
            new DateOnly(2026, 11, 1), scheduledStart - TimeSpan.FromHours(48), 0m, terms));
        Assert.Equal(0m, fromTerms.AwardAmount);
        Assert.Equal(CareHomeRefundScheduleBasis.FacilityTerms, fromTerms.Basis);
        Assert.Equal(CareHomeRefundPolicyVersion.ScheduledStartVersion, fromTerms.PolicyVersion.Version);
        Assert.Equal(CareHomeRefundPolicyVersion.PolicyDocumentEffectiveOn, fromTerms.PolicyVersion.DocumentEffectiveOnUtc);

        var fromDefault = CareHomeRefundPolicy.Decide(new CareHomeRefundPolicyInput(
            new DateOnly(2026, 11, 1), scheduledStart - TimeSpan.FromHours(48), 0m, null));
        Assert.Equal(0m, fromDefault.AwardAmount);
        Assert.Equal(CareHomeRefundScheduleBasis.DefaultSchedule, fromDefault.Basis);
    }

    [Fact]
    public void Decide_validates_the_input_instead_of_guessing()
    {
        DateTime local = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Local);

        Assert.Throws<DomainException>(() => CareHomeRefundPolicy.Decide(
            new CareHomeRefundPolicyInput(new DateOnly(2026, 11, 1), local, 100m, null)));
        Assert.Throws<DomainException>(() => CareHomeRefundPolicy.Decide(
            new CareHomeRefundPolicyInput(new DateOnly(2026, 11, 1), Start, -0.01m, null)));
        Assert.Throws<DomainException>(() => CareHomeRefundPolicy.Decide(
            new CareHomeRefundPolicyInput(default, Start, 100m, null)));
    }

    [Fact]
    public void Snapshot_accepts_revision2_with_terms_rejects_legacy_terms_and_unreadable_terms()
    {
        string termsJson = CareHomeCancellationTerms.From([new CareHomeCancellationTermTier(100, 48)]).ToJson();

        CareHomePolicySnapshot valid = CareHomePolicySnapshot.From(
            CareHomeRefundPolicyVersion.ForNewBooking(true), null, termsJson);
        Assert.Equal(CareHomeRefundPolicyVersion.ScheduledStartVersion, valid.PolicyVersion);
        Assert.Equal(CareHomeRefundPolicyVersion.PolicyDocumentEffectiveOn, valid.DocumentEffectiveOn);
        Assert.Equal(termsJson, valid.CancellationTermsJson);

        Assert.Throws<DomainException>(() => CareHomePolicySnapshot.From(
            CareHomeRefundPolicyVersion.ForNewBooking(false), null, termsJson));
        Assert.Throws<DomainException>(() => CareHomePolicySnapshot.From(
            CareHomeRefundPolicyVersion.ForNewBooking(true), null, "{broken"));
        CareHomePolicySnapshot legacy = CareHomePolicySnapshot.From(
            CareHomeRefundPolicyVersion.ForNewBooking(false), null, null);
        Assert.Equal(CareHomeRefundPolicyVersion.LegacyVersion, legacy.PolicyVersion);
        Assert.Equal(default, legacy.DocumentEffectiveOn);
        Assert.Null(legacy.CancellationTermsJson);
    }

    [Fact]
    public void Persisted_versions_reject_unknown_revisions_and_v2_without_the_document_date()
    {
        Assert.Throws<DomainException>(() => CareHomeRefundPolicyVersion.FromPersisted(3, new DateOnly(2026, 10, 22)));
        Assert.Throws<DomainException>(() => CareHomeRefundPolicyVersion.FromPersisted(0, default));
        Assert.Throws<DomainException>(() => CareHomeRefundPolicyVersion.FromPersisted(2, default));

        Assert.True(CareHomeRefundPolicyVersion.FromPersisted(1, default).IsLegacy);
        CareHomeRefundPolicyVersion v2 = CareHomeRefundPolicyVersion.FromPersisted(2, new DateOnly(2026, 10, 22));
        Assert.True(v2.IsScheduledStart);
    }

    [Fact]
    public void New_bookings_stay_legacy_while_the_scheduled_start_policy_is_dormant()
    {
        CareHomeRefundPolicyVersion dormant = CareHomeRefundPolicyVersion.ForNewBooking(false);
        Assert.Equal(CareHomeRefundPolicyVersion.LegacyVersion, dormant.Version);
        Assert.True(dormant.IsLegacy);
        Assert.Equal(default, dormant.DocumentEffectiveOnUtc);

        CareHomeRefundPolicyVersion activated = CareHomeRefundPolicyVersion.ForNewBooking(true);
        Assert.Equal(CareHomeRefundPolicyVersion.ScheduledStartVersion, activated.Version);
        Assert.Equal(CareHomeRefundPolicyVersion.PolicyDocumentEffectiveOn, activated.DocumentEffectiveOnUtc);

        // The lane ships with the money-affecting switch off (Owner decision #7);
        // activation is a separate, reviewed flip.
        Assert.False(CareHomeRefundPolicySnapshotResolver.ScheduledStartPolicyActivated);
    }

    [Fact]
    public void Snapshot_resolver_returns_a_legacy_snapshot_without_terms_while_dormant()
    {
        CareHomeFacility facility = CareHomeFacility.CreateDraft(UserId.New(), DateTime.UtcNow);

        CareHomePolicySnapshot resolved = CareHomeRefundPolicySnapshotResolver.Resolve(facility, DateTime.UtcNow);

        Assert.Equal(CareHomeRefundPolicyVersion.LegacyVersion, resolved.PolicyVersion);
        Assert.Equal(default, resolved.DocumentEffectiveOn);
        Assert.Null(resolved.CancellationTermsJson);
        Assert.Null(resolved.ProfileRevisionId);
    }
}
