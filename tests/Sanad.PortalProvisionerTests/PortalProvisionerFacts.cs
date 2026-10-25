using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Application;
using Sanad.PortalProvisioner;

namespace Sanad.PortalProvisionerTests;

/// <summary>
/// Focused facts for the S3a rehearsal provisioning tool: classification
/// stability, plan determinism, identity determinism, fail-closed expectation
/// parsing, and the structural inertness seams. All offline — no database,
/// no network, no provider.
/// </summary>
public sealed class PortalProvisionerFacts
{
    private static readonly IReadOnlyList<string> ExpectedDataOnlyGroups =
    [
        "carehome-facility-draft",
        "carehome-paid-awaiting-decision",
        "carehome-refund-ambiguity",
        "carehome-evidenced-external-refund",
        "carehome-booking-extension",
        "carehome-checkout-dispute",
        "caregiver-confirmed-booking",
        "caregiver-completed-booking",
        "caregiver-refund-ambiguity",
        "subscription-and-duplicate-claim",
        "families-care-records",
        "family-private-access",
        "complaint-received",
        "complaint-investigating"
    ];

    [Fact]
    public void Classification_IsStable_And_Gates_Seam_Groups()
    {
        Assert.Equal(ExpectedDataOnlyGroups, PortalProvisionGroups.DataOnlyGroups);
        Assert.Equal(
            new[]
            {
                "caregiver-refund-failed",
                "carehome-refund-failed",
                "duplicate-claim-approved-awaiting-execution",
                "duplicate-claim-ambiguous"
            },
            PortalProvisionGroups.SeamGroups);

        // The four seam groups hold refund-send-armed / execution-state rows:
        // they are buildable ONLY behind the operator's --seam-deployed
        // declaration that the deployed revision runs the FixtureRefundSeam
        // allowlist for their allowlisted transaction ids.
        Assert.DoesNotContain("caregiver-refund-failed", PortalProvisionGroups.DataOnlyGroups);
        Assert.DoesNotContain("carehome-refund-failed", PortalProvisionGroups.DataOnlyGroups);
        Assert.Equal(14, PortalProvisionGroups.DataOnlyGroups.Count);
        Assert.Equal(4, PortalProvisionGroups.SeamGroups.Count);
        Assert.Empty(PortalProvisionGroups.DataOnlyGroups.Intersect(PortalProvisionGroups.SeamGroups));
    }

    [Fact]
    public void Plan_IsByteIdentical_ForIdenticalInputs()
    {
        DateTime instant = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        string first = PortalProvisionPlan.Serialize(
            PortalProvisionPlan.Build("r2", instant, "4690b5b2", "rehearsal", []));
        string second = PortalProvisionPlan.Serialize(
            PortalProvisionPlan.Build("r2", instant, "4690b5b2", "rehearsal", []));

        Assert.Equal(first, second);
        Assert.Contains("\"markerPrefix\": \"spf\"", first);
    }

    [Fact]
    public void Plan_Changes_WithInstant_Tag_And_Revision()
    {
        DateTime instant = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        string basePlan = PortalProvisionPlan.Serialize(
            PortalProvisionPlan.Build("r2", instant, "4690b5b2", "rehearsal", []));
        string otherInstant = PortalProvisionPlan.Serialize(
            PortalProvisionPlan.Build("r2", instant.AddMinutes(1), "4690b5b2", "rehearsal", []));
        string otherTag = PortalProvisionPlan.Serialize(
            PortalProvisionPlan.Build("r3", instant, "4690b5b2", "rehearsal", []));
        string otherRevision = PortalProvisionPlan.Serialize(
            PortalProvisionPlan.Build("r2", instant, "deadbeef", "rehearsal", []));

        Assert.NotEqual(basePlan, otherInstant);
        Assert.NotEqual(basePlan, otherTag);
        Assert.NotEqual(basePlan, otherRevision);
    }

    [Fact]
    public void Plan_Refuses_Seam_Groups_Without_The_Declaration_And_Plans_Them_With_It()
    {
        DateTime instant = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

        foreach (string seam in PortalProvisionGroups.SeamGroups)
        {
            var refused = Assert.Throws<InvalidOperationException>(
                () => PortalProvisionPlan.Build("r2", instant, "4690b5b2", "rehearsal", [seam]));
            Assert.Contains("requires --seam-deployed", refused.Message);
        }

        Assert.Throws<InvalidOperationException>(
            () => PortalProvisionPlan.Build("r2", instant, "4690b5b2", "rehearsal", ["not-a-group"]));

        // With the operator's declaration, every seam group plans and carries
        // its allowlisted transaction id and scripted outcome in the manifest.
        PortalProvisionPlanReport seamPlan = PortalProvisionPlan.Build(
            "r2", instant, "4690b5b2", "rehearsal",
            [.. PortalProvisionGroups.SeamGroups],
            seamDeployed: true);
        Assert.True(seamPlan.SeamDeployed);
        Assert.Equal(
            [.. PortalProvisionGroups.SeamGroups.Order()],
            [.. seamPlan.PlannedGroups.Select(group => group.Group).Order()]);
        string seamJson = PortalProvisionPlan.Serialize(seamPlan);
        Assert.Contains(PortalProvisionIdentity.SeamCaregiverFailedTransactionId("r2"), seamJson);
        Assert.Contains(PortalProvisionIdentity.SeamCareHomeFailedTransactionId("r2"), seamJson);
        Assert.Contains(PortalProvisionIdentity.SeamClaimApprovedTransactionId("r2"), seamJson);
        Assert.Contains(PortalProvisionIdentity.SeamClaimAmbiguousTransactionId("r2"), seamJson);
        string seamDetails = string.Join(
            " ",
            seamPlan.PlannedGroups.SelectMany(group => group.PlannedRecords).Select(record => record.Detail));
        Assert.Contains("'Rejected'", seamDetails);
        Assert.Contains("'Unknown'", seamDetails);
    }

    [Fact]
    public void Plan_Seam_Mode_IsByteIdentical_And_Identity_Derivations_AreStable()
    {
        DateTime instant = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        string first = PortalProvisionPlan.Serialize(
            PortalProvisionPlan.Build("r2", instant, "4690b5b2", "rehearsal", [], seamDeployed: true));
        string second = PortalProvisionPlan.Serialize(
            PortalProvisionPlan.Build("r2", instant, "4690b5b2", "rehearsal", [], seamDeployed: true));
        Assert.Equal(first, second);

        // Deterministic seam allowlist ids: the care-home Failed group's id is
        // the numeric StableNumber of its marker (a care-home Paymob
        // transaction id is a long); the others are fixed marker strings.
        string careHomeTxn = PortalProvisionIdentity.SeamCareHomeFailedTransactionId("r2");
        Assert.True(long.TryParse(careHomeTxn, out long numeric) && numeric > 0);
        Assert.Equal(
            PortalProvisionIdentity.StableNumber("spf:r2:carehome-refund-failed")
                .ToString(System.Globalization.CultureInfo.InvariantCulture),
            careHomeTxn);
        Assert.Equal("spf-r2-seam-caregiver-failed-txn", PortalProvisionIdentity.SeamCaregiverFailedTransactionId("r2"));
        Assert.Equal("spf-r2-seam-claim-approved-txn", PortalProvisionIdentity.SeamClaimApprovedTransactionId("r2"));
        Assert.Equal("spf-r2-seam-claim-ambiguous-txn", PortalProvisionIdentity.SeamClaimAmbiguousTransactionId("r2"));
        Assert.Equal("spf-r2-seam-duplicate-claim-approved", PortalProvisionIdentity.SeamClaimApprovedIdempotencyKey("r2"));
        Assert.Equal("spf-r2-seam-duplicate-claim-ambiguous", PortalProvisionIdentity.SeamClaimAmbiguousIdempotencyKey("r2"));

        // The two seam capture roles have their own ordinals so their phone
        // identities never collide with any other run role.
        Assert.Equal(10, PortalProvisionIdentity.RoleOrdinal("seam.claim.approved"));
        Assert.Equal(11, PortalProvisionIdentity.RoleOrdinal("seam.claim.ambiguous"));
    }

    [Fact]
    public void Plan_Covers_Exactly_The_Requested_Groups()
    {
        DateTime instant = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        PortalProvisionPlanReport full = PortalProvisionPlan.Build(
            "r2", instant, "rev", "rehearsal", []);

        Assert.Equal(PortalProvisionGroups.DataOnlyGroups, full.PlannedGroups.Select(group => group.Group));
        Assert.Equal(PortalProvisionGroups.SeamGroups, full.SeamGatedGroupsNotProvisioned);
        Assert.False(full.SeamDeployed);

        PortalProvisionPlanReport subset = PortalProvisionPlan.Build(
            "r2", instant, "rev", "rehearsal",
            [PortalProvisionGroups.CaregiverRefundAmbiguity]);
        var single = Assert.Single(subset.PlannedGroups);
        Assert.Equal(PortalProvisionGroups.CaregiverRefundAmbiguity, single.Group);
    }

    [Fact]
    public void Identity_Derivation_IsDeterministic_And_TagSeparable()
    {
        string email1 = PortalProvisionIdentity.FixtureEmail("r2", "family.owner");
        string email2 = PortalProvisionIdentity.FixtureEmail("r2", "family.owner");
        Assert.Equal(email1, email2);
        Assert.Equal("r2.family.owner@test.sanad.local", email1);

        Assert.NotEqual(
            PortalProvisionIdentity.FixtureEmail("r2", "family.owner"),
            PortalProvisionIdentity.FixtureEmail("r3", "family.owner"));
        Assert.NotEqual(
            PortalProvisionIdentity.DeterministicPhone("r2", "family.owner"),
            PortalProvisionIdentity.DeterministicPhone("r3", "family.owner"));

        // Per-role ordinals make same-tag role collisions impossible.
        var phones = new[] { "family.owner", "caregiver", "carehome.owner", "support", "superadmin", "elderly", "shutdown.owner", "carehome.owner2" }
            .Select(role => PortalProvisionIdentity.DeterministicPhone("r2", role))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(8, phones.Count);

        Assert.StartsWith("+2010", PortalProvisionIdentity.DeterministicPhone("r2", "family.owner"), StringComparison.Ordinal);

        // Deterministic markers carry the spf prefix and the tag.
        Assert.Equal("spf:r2:caregiver-refund-ambiguity", PortalProvisionIdentity.GroupMarker("r2", PortalProvisionGroups.CaregiverRefundAmbiguity));
        Assert.Equal("[r2:family.owner]", PortalProvisionIdentity.OwnershipMarker("r2", "family.owner"));
    }

    [Theory]
    [InlineData(null, "5432", "db", "rev", "rehearsal", "care_homes=head --expect-host is required")]
    public void Guard_Parse_Refuses_Missing_Fields(
        string? host, string port, string database, string revision, string targetKind, string _)
    {
        List<string> pairs = RequiredHeadPairs();
        PortalProvisionExpectations expectations = ProvisionerTargetGuard.Parse(
            host, port, database, revision, targetKind, tunnelDeclared: false, pairs, out IReadOnlyList<string> refusals);

        Assert.Contains(refusals, message => message.Contains("--expect-host is required", StringComparison.Ordinal));
        Assert.Equal(string.Empty, expectations.Host);
    }

    [Fact]
    public void Guard_Parse_Refuses_Production()
    {
        ProvisionerTargetGuard.Parse(
            "10.0.0.9", "5432", "db", "rev", "production", tunnelDeclared: false, RequiredHeadPairs(),
            out IReadOnlyList<string> refusals);

        Assert.Contains(refusals, message => message.Contains("Production is refused outright", StringComparison.Ordinal));
    }

    [Fact]
    public void Guard_Parse_Refuses_Unexpected_TargetKind_And_Bad_Port()
    {
        ProvisionerTargetGuard.Parse(
            "h", "99999", "db", "rev", "rehearsal", tunnelDeclared: false, RequiredHeadPairs(),
            out IReadOnlyList<string> refusals);
        Assert.Contains(refusals, message => message.Contains("--expect-port", StringComparison.Ordinal));

        ProvisionerTargetGuard.Parse(
            "h", "5432", "db", "rev", "local-dev", tunnelDeclared: false, RequiredHeadPairs(),
            out refusals);
        Assert.Contains(refusals, message => message.Contains("--target-kind", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void Guard_Parse_Refuses_Protected_Local_Databases(string loopbackHost)
    {
        ProvisionerTargetGuard.Parse(
            loopbackHost, "5432", "SanadDb", "rev", "rehearsal", tunnelDeclared: false, RequiredHeadPairs(),
            out IReadOnlyList<string> refusals);
        Assert.Contains(refusals, message => message.Contains("protected local database", StringComparison.Ordinal));

        ProvisionerTargetGuard.Parse(
            loopbackHost, "5432", "SanadIntegrationDb", "rev", "rehearsal", tunnelDeclared: false, RequiredHeadPairs(),
            out refusals);
        Assert.Contains(refusals, message => message.Contains("protected local database", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    public void Guard_Parse_Allows_Protected_Name_Only_Under_Tunnel_Declaration(string loopbackHost)
    {
        // The tunneled rehearsal shape: the connection string points at the
        // tunnel's LOOPBACK terminus — exactly the hosts the direct path
        // refuses — with the protected database name SanadDb. Parse allows
        // the shape ONLY with the explicit --tunnel-declared operator
        // declaration; the runtime cluster-identifier check is then what
        // separates the rehearsal cluster from the local protected database.
        ProvisionerTargetGuard.Parse(
            loopbackHost, "5432", "SanadDb", "rev", "rehearsal", tunnelDeclared: true, RequiredHeadPairs(),
            out IReadOnlyList<string> refusals);
        Assert.Empty(refusals);

        // The same loopback terminus stays refused without the declaration.
        ProvisionerTargetGuard.Parse(
            loopbackHost, "5432", "SanadDb", "rev", "rehearsal", tunnelDeclared: false, RequiredHeadPairs(),
            out refusals);
        Assert.Contains(refusals, message => message.Contains("protected local database", StringComparison.Ordinal));
    }

    [Fact]
    public void Guard_Parse_Refuses_Missing_Or_Duplicate_Module_Heads()
    {
        ProvisionerTargetGuard.Parse(
            "h", "5432", "db", "rev", "rehearsal", tunnelDeclared: false, ["families=20261023131500_AddCareRecords"],
            out IReadOnlyList<string> refusals);
        Assert.Equal(8, refusals.Count(message => message.Contains("missing the required module", StringComparison.Ordinal)));

        List<string> pairs = RequiredHeadPairs();
        pairs.Add("families=20261023131313_Duplicate");
        ProvisionerTargetGuard.Parse(
            "h", "5432", "db", "rev", "rehearsal", tunnelDeclared: false, pairs, out refusals);
        Assert.Contains(refusals, message => message.Contains("passed twice", StringComparison.Ordinal));
    }

    [Fact]
    public void Guard_Parse_Accepts_Complete_Rehearsal_Expectations()
    {
        PortalProvisionExpectations expectations = ProvisionerTargetGuard.Parse(
            "72.62.92.144", "5432", "SanadDb", "4690b5b2e46a311bbb64f76c41c81769a34d4832",
            "rehearsal", tunnelDeclared: false, RequiredHeadPairs(), out IReadOnlyList<string> refusals);

        Assert.Empty(refusals);
        Assert.Equal(9, expectations.MigrationHeads.Count);
        Assert.Equal("rehearsal", expectations.TargetKind);
        Assert.False(expectations.TunnelDeclared);
    }

    // ----------------------------------------------------------------------
    // Reached-identity decision matrix (review defect 2): tunnel declaration,
    // cluster system identifier, and the unchanged direct-connection path.
    // ----------------------------------------------------------------------

    private static PortalProvisionExpectations TunnelExpectations(
        string host = "72.62.92.144",
        string database = "SanadDb",
        bool tunnelDeclared = true)
        => ProvisionerTargetGuard.Parse(
            host, "5432", database, "4690b5b2", "rehearsal", tunnelDeclared,
            RequiredHeadPairs(), out _);

    private static ProvisionerTargetGuard.PortalReachedIdentity Reached(
        string? address, string? identifier = "7246867520585489674", bool readable = true)
        => new("SanadDb", address, 5432, identifier, readable, null);    [Fact]
    public void Guard_Identity_Tunnel_Accepts_Loopback_Terminus_With_Matching_Identifier()
    {
        IReadOnlyList<string> refusals = ProvisionerTargetGuard.EvaluateReachedIdentity(
            TunnelExpectations(), "7246867520585489674", Reached("::1/128"));

        Assert.Empty(refusals);
    }

    [Fact]
    public void Guard_Identity_Refuses_Tunnel_Shapes_Without_Declaration()
    {
        // Same reached loopback, but no --tunnel-declared: strict address
        // matching decides and refuses; no identifier can paper over it.
        IReadOnlyList<string> refusals = ProvisionerTargetGuard.EvaluateReachedIdentity(
            TunnelExpectations(tunnelDeclared: false), "7246867520585489674", Reached("::1/128"));

        Assert.Contains(refusals, message => message.Contains("server address mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public void Guard_Identity_Refuses_On_Identifier_Mismatch_Even_Tunneled()
    {
        // The loopback protection: a tunnel-declared loopback reach whose
        // cluster fingerprint differs (e.g. the LOCAL SanadDb) is refused.
        IReadOnlyList<string> refusals = ProvisionerTargetGuard.EvaluateReachedIdentity(
            TunnelExpectations(), "7246867520585489674", Reached("::1/128", "1111111111111111111"));

        Assert.Contains(
            refusals,
            message => message.Contains("cluster system identifier mismatch", StringComparison.Ordinal));
        // The refusal must not leak the identifier values.
        Assert.DoesNotContain(refusals, message => message.Contains("7246867520585489674", StringComparison.Ordinal));
        Assert.DoesNotContain(refusals, message => message.Contains("1111111111111111111", StringComparison.Ordinal));
    }

    [Fact]
    public void Guard_Identity_Refuses_On_Missing_Identifier()
    {
        IReadOnlyList<string> refusals = ProvisionerTargetGuard.EvaluateReachedIdentity(
            TunnelExpectations(), null, Reached("::1/128"));

        Assert.Contains(
            refusals,
            message => message.Contains("cluster system identifier is mandatory", StringComparison.Ordinal));
    }

    [Fact]
    public void Guard_Identity_Refuses_On_Unreadable_Identifier_Naming_Privilege()
    {
        IReadOnlyList<string> refusals = ProvisionerTargetGuard.EvaluateReachedIdentity(
            TunnelExpectations(), "7246867520585489674",
            new ProvisionerTargetGuard.PortalReachedIdentity(
                "SanadDb", "::1/128", 5432, null, false, "42501: permission denied for function"));

        Assert.Contains(
            refusals,
            message => message.Contains("cannot read pg_control_system()", StringComparison.Ordinal));
        Assert.Contains(refusals, message => message.Contains("pg_read_control_data", StringComparison.Ordinal));
    }

    [Fact]
    public void Guard_Identity_Direct_Path_Stays_Strict()
    {
        // Direct (non-tunneled) rehearsal reach: exact address match still
        // holds, and the cluster identifier is still mandatory.
        IReadOnlyList<string> matching = ProvisionerTargetGuard.EvaluateReachedIdentity(
            TunnelExpectations(), "7246867520585489674", Reached("72.62.92.144"));
        Assert.Empty(matching);

        IReadOnlyList<string> wrongAddress = ProvisionerTargetGuard.EvaluateReachedIdentity(
            TunnelExpectations(tunnelDeclared: false), "7246867520585489674", Reached("10.9.9.9"));
        Assert.Contains(wrongAddress, message => message.Contains("server address mismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Refusing_Gateway_Never_Allows_A_Refund_Path()
    {
        var preparation = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((ISubscriptionRefundGateway)RefusingRefundGateway.Instance).PrepareAsync(
                Guid.NewGuid(), CancellationToken.None));
        Assert.Contains("inertness guard", preparation.Message, StringComparison.Ordinal);

        var refund = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ((ISubscriptionRefundGateway)RefusingRefundGateway.Instance).RefundAsync(
                "charge-1", "EGP", 100, CancellationToken.None));
        Assert.Contains("inertness guard", refund.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionerClock_Pins_The_Passed_Instant()
    {
        DateTime utc = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, new ProvisionerClock(utc).UtcNow);
        Assert.Equal(DateTimeKind.Utc, new ProvisionerClock(utc).UtcNow.Kind);

        DateTime unspecified = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Unspecified);
        IDateTimeProvider clock = new ProvisionerClock(unspecified);
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
        Assert.Equal(unspecified.Ticks, clock.UtcNow.Ticks);
    }

    [Fact]
    public void Offline_Domain_SelfCheck_Passes()
    {
        DateTime instant = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
        PortalProvisionDomainChecks.PortalProvisionDomainCheckReport report = PortalProvisionDomainChecks.Run(instant);

        Assert.True(report.Passed);
        Assert.NotEmpty(report.Checks);
    }

    // ----------------------------------------------------------------------
    // Run-wide refund-inertness invariant (review defect 1): the armed shapes
    // the admin retry routes would send to the provider, and the claim-held /
    // ambiguous / submitted shapes that refuse pre-gateway.
    // ----------------------------------------------------------------------

    private static readonly DateTime FixtureNow = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    private static CareHomeBooking NewPaidCareHomeBooking(string marker)
    {
        CareHomeBooking booking = CareHomeBooking.Create(
            CareHomeId.New(),
            UserId.New(),
            FamilyId.New(),
            ElderlyId.New(),
            Guid.NewGuid(),
            DateOnly.FromDateTime(FixtureNow).AddDays(2),
            baseAmount: 1000m,
            feeAmount: 100m,
            taxAmount: 50m,
            ruleVersion: 4,
            arabicName: "الجد التجريبي",
            englishName: "Portal Fixture Grandfather",
            age: 75,
            medicalJson: null,
            contactName: "Portal Fixture Family Owner",
            contactPhone: "+201000000001",
            relationship: "Grandfather",
            careNotes: marker,
            utcNow: FixtureNow);
        booking.MarkPaid(2026102501L, FixtureNow.AddMinutes(-30));
        return booking;
    }

    [Fact]
    public void Invariant_Flags_An_Unclaimed_Rejected_CareHome_Booking_As_SendArmed()
    {
        // This is EXACTLY the rejected-example shape the prior S3a build
        // stored (review defect 1): Status=RefundPending, RefundStatus=Pending,
        // RefundClaimedOnUtc=null → IsRefundSendArmed is true → the refund
        // retry would claim-and-send it to the provider.
        CareHomeBooking booking = NewPaidCareHomeBooking("spf:r2:carehome-paid-awaiting-decision:rejected");
        booking.Reject("Synthetic local fixture rejection (no provider movement).", FixtureNow.AddMinutes(-29));

        Assert.True(booking.IsRefundSendArmed);

        IReadOnlyList<string> violations = PortalProvisionInvariants.Evaluate(
            [booking], [], []);
        Assert.Contains(violations, message => message.Contains("refund-send-armed", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariant_Accepts_The_Claim_Held_Rejected_CareHome_Shape()
    {
        // The corrected fixture shape: the SAME rejected booking with its
        // refund attempt CLAIMED right after Reject — still RefundPending/
        // Pending, not ambiguous, not completed — is never send-armed.
        CareHomeBooking booking = NewPaidCareHomeBooking("spf:r2:carehome-paid-awaiting-decision:rejected");
        booking.Reject("Synthetic local fixture rejection (no provider movement).", FixtureNow.AddMinutes(-29));
        Assert.True(booking.TryClaimRefund(FixtureNow.AddMinutes(-28)));

        Assert.False(booking.IsRefundSendArmed);
        Assert.Equal(CareHomeBookingStatus.RefundPending, booking.Status);
        Assert.Equal(CareHomeRefundStatus.Pending, booking.RefundStatus);
        Assert.NotNull(booking.RefundClaimedOnUtc);
        Assert.False(booking.IsRefundOutcomeAmbiguous);

        IReadOnlyList<string> violations = PortalProvisionInvariants.Evaluate(
            [booking], [], []);
        Assert.Empty(violations);
    }

    [Fact]
    public void Invariant_Flags_A_Failed_CareHome_Refund_Row()
    {
        CareHomeBooking booking = NewPaidCareHomeBooking("spf:r2:carehome-x");
        booking.Accept(FixtureNow.AddMinutes(-29));
        booking.CancelByFacility("Synthetic local fixture cancellation.", FixtureNow.AddMinutes(-20));
        Assert.True(booking.TryClaimRefund(FixtureNow.AddMinutes(-19)));
        booking.MarkRefundFailed("Synthetic local failure marker (no provider was contacted).", FixtureNow.AddMinutes(-18));

        IReadOnlyList<string> violations = PortalProvisionInvariants.Evaluate(
            [booking], [], []);
        Assert.Contains(violations, message => message.Contains("Failed refund fixture row", StringComparison.Ordinal));
        // MarkRefundFailed also clears the claim, so the row is armed again.
        Assert.Contains(violations, message => message.Contains("refund-send-armed", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariant_Flags_A_Failed_Resolved_Families_Booking()
    {
        Booking booking = PortalProvisionFixtureBookings.PaidCancelled();

        Assert.Equal(BookingRefundState.Failed, BookingRefundStates.Resolve(booking));

        IReadOnlyList<string> violations = PortalProvisionInvariants.Evaluate(
            [], [booking], []);
        Assert.Contains(
            violations,
            message => message.Contains("resolves to refund state Failed", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariant_Accepts_Claim_Held_And_Ambiguous_Families_Shape()
    {
        // The shipped caregiver-refund-ambiguity shape: a paid CONFIRMED
        // booking whose in-flight attempt carries an ambiguity marker — the
        // admin retry refuses it before the gateway (AdminBookingCommands).
        Booking booking = PortalProvisionFixtureBookings.ConfirmedAmbiguous();

        Assert.Equal(BookingRefundState.Pending, BookingRefundStates.Resolve(booking));

        IReadOnlyList<string> violations = PortalProvisionInvariants.Evaluate(
            [], [booking], []);
        Assert.Empty(violations);
    }

    [Fact]
    public void Invariant_Refuses_Approved_Or_Executing_Duplicate_Claims()
    {
        void PAssert(IReadOnlyList<string> list, string fragment) =>
            Assert.Contains(list, message => message.Contains(fragment, StringComparison.Ordinal));
        var submitted = new FixtureClaimSnapshot(Guid.NewGuid(), DuplicateChargeClaimState.Submitted, DuplicateChargeExecutionState.None);
        var approved = new FixtureClaimSnapshot(Guid.NewGuid(), DuplicateChargeClaimState.Approved, DuplicateChargeExecutionState.AwaitingExecution);
        var executing = new FixtureClaimSnapshot(Guid.NewGuid(), DuplicateChargeClaimState.Approved, DuplicateChargeExecutionState.Executing);
        var failedAttempt = new FixtureClaimSnapshot(Guid.NewGuid(), DuplicateChargeClaimState.Approved, DuplicateChargeExecutionState.Failed);

        Assert.Empty(PortalProvisionInvariants.Evaluate([], [], [submitted]));

        IReadOnlyList<string> violations = PortalProvisionInvariants.Evaluate(
            [], [], [approved, executing, failedAttempt]);
        // Each non-submitted claim trips BOTH its state rule and its
        // execution-phase rule — fail-closed double reporting is intended.
        Assert.Equal(6, violations.Count);
        PAssert(violations, "is Approved");
        PAssert(violations, "execution state AwaitingExecution");
        PAssert(violations, "execution state Executing");
        PAssert(violations, "execution state Failed");

    }

    [Fact]
    public void Invariant_All_Safe_Shipped_Shape_Together_Passes()
    {
        CareHomeBooking claimed = NewPaidCareHomeBooking("spf:r2:carehome-refund-ambiguity");
        claimed.Accept(FixtureNow.AddMinutes(-29));
        claimed.CancelByFacility("Synthetic local fixture cancellation.", FixtureNow.AddMinutes(-20));
        Assert.True(claimed.TryClaimRefund(FixtureNow.AddMinutes(-19)));
        claimed.MarkRefundOutcomeAmbiguous(
            "Synthetic local ambiguity marker: no provider was contacted.",
            FixtureNow.AddMinutes(-18));

        IReadOnlyList<string> violations = PortalProvisionInvariants.Evaluate(
            [claimed],
            [PortalProvisionFixtureBookings.ConfirmedAmbiguous()],
            [new FixtureClaimSnapshot(Guid.NewGuid(), DuplicateChargeClaimState.Submitted, DuplicateChargeExecutionState.None)]);

        Assert.Empty(violations);
    }

    [Fact]
    public void Invariant_Exempts_Exactly_The_Seam_Rows_Only_Under_The_Declaration()
    {
        // The seam care-home Failed row: claim-released Failed → armed, i.e.
        // EXACTLY the shape the invariant exists to forbid. Under the
        // operator's --seam-deployed declaration (exemptions naming the seam
        // marker) it is the one excusable row; without the declaration the
        // exemption set is empty and the row is flagged.
        CareHomeBooking seamCareHome = NewPaidCareHomeBooking("spf:r2:carehome-refund-failed");
        seamCareHome.Reject("Synthetic local fixture rejection (no provider movement).", FixtureNow.AddMinutes(-29));
        Assert.True(seamCareHome.TryClaimRefund(FixtureNow.AddMinutes(-28)));
        seamCareHome.MarkRefundFailed("Synthetic scripted fixture refund rejection (seam; no provider call).",
            FixtureNow.AddMinutes(-27));

        IReadOnlyList<string> withoutDeclaration = PortalProvisionInvariants.Evaluate([seamCareHome], [], []);
        Assert.Contains(withoutDeclaration, message => message.Contains("refund-send-armed", StringComparison.Ordinal));

        IReadOnlyList<string> withDeclaration = PortalProvisionInvariants.Evaluate(
            [seamCareHome], [], [],
            new SeamRefundExemptions(
                ["spf:r2:carehome-refund-failed"],
                [],
                []));
        Assert.Empty(withDeclaration);
    }

    [Fact]
    public void Invariant_Exemptions_Never_Cover_Normal_Or_NearMiss_CareHome_Rows()
    {
        // A normal group's row with the SAME exemption set present is still
        // flagged: the exemption is an EXACT marker whitelist.
        CareHomeBooking normalGroup = NewPaidCareHomeBooking("spf:r2:carehome-refund-ambiguity");
        normalGroup.Reject("Synthetic local fixture rejection (no provider movement).", FixtureNow.AddMinutes(-29));
        Assert.True(normalGroup.TryClaimRefund(FixtureNow.AddMinutes(-28)));
        normalGroup.MarkRefundFailed("Synthetic scripted fixture refund rejection (seam; no provider call).",
            FixtureNow.AddMinutes(-27));

        // A near-miss marker (extra suffix → a different row) is never
        // covered by the exact seam marker either.
        CareHomeBooking nearMiss = NewPaidCareHomeBooking("spf:r2:carehome-refund-failed:extra");
        nearMiss.Reject("Synthetic local fixture rejection (no provider movement).", FixtureNow.AddMinutes(-29));
        Assert.True(nearMiss.TryClaimRefund(FixtureNow.AddMinutes(-28)));
        nearMiss.MarkRefundFailed("Synthetic scripted fixture refund rejection (seam; no provider call).",
            FixtureNow.AddMinutes(-27));

        IReadOnlyList<string> violations = PortalProvisionInvariants.Evaluate(
            [normalGroup, nearMiss], [], [],
            new SeamRefundExemptions(
                ["spf:r2:carehome-refund-failed"],
                [],
                []));
        Assert.Contains(violations, message => message.Contains("refund-send-armed", StringComparison.Ordinal));
        Assert.Equal(4, violations.Count); // both rows trip BOTH rules
    }

    [Fact]
    public void Invariant_SeamExemptions_Never_Cover_Normal_Or_NearMiss_Families_Rows()
    {
        Booking seamRow = PaidCancelledFamiliesBooking("spf:r2:caregiver-refund-failed");
        Booking normalRow = PaidCancelledFamiliesBooking("spf:r2:caregiver-confirmed-booking");
        Booking nearMiss = PaidCancelledFamiliesBooking("spf:r2:caregiver-refund-failed-txn");

        IReadOnlyList<string> withoutDeclaration = PortalProvisionInvariants.Evaluate(
            [], [seamRow, normalRow, nearMiss], []);
        // Fail-closed: without --seam-deployed every Failed row is flagged.
        Assert.Equal(3, withoutDeclaration.Count);

        IReadOnlyList<string> withDeclaration = PortalProvisionInvariants.Evaluate(
            [], [seamRow, normalRow, nearMiss], [],
            new SeamRefundExemptions(
                [],
                ["spf:r2:caregiver-refund-failed"],
                []));
        // ONLY the exact seam marker row is excused; the normal group's row
        // and the near-miss marker row stay flagged (messages carry booking ids).
        Assert.Equal(2, withDeclaration.Count);
        Assert.Contains(
            withDeclaration,
            message => message.Contains(normalRow.Id.Value.ToString(), StringComparison.Ordinal));
        Assert.Contains(
            withDeclaration,
            message => message.Contains(nearMiss.Id.Value.ToString(), StringComparison.Ordinal));
    }

    [Fact]
    public void Invariant_SeamExemptions_Exact_Keys_Only_Cover_The_Two_Seam_Claims()
    {
        var approvedSeamClaim = new FixtureClaimSnapshot(
            Guid.NewGuid(), DuplicateChargeClaimState.Approved,
            DuplicateChargeExecutionState.AwaitingExecution,
            "spf-r2-seam-duplicate-claim-approved");
        var ambiguousSeamClaim = new FixtureClaimSnapshot(
            Guid.NewGuid(), DuplicateChargeClaimState.Approved,
            DuplicateChargeExecutionState.Ambiguous,
            "spf-r2-seam-duplicate-claim-ambiguous");
        var normalSubmitted = new FixtureClaimSnapshot(
            Guid.NewGuid(), DuplicateChargeClaimState.Submitted,
            DuplicateChargeExecutionState.None, "spf-r2-duplicate-claim");
        var foreignApproved = new FixtureClaimSnapshot(
            Guid.NewGuid(), DuplicateChargeClaimState.Approved,
            DuplicateChargeExecutionState.AwaitingExecution,
            "spf-r2-seam-duplicate-claim-approved-suffix");

        IReadOnlyList<string> violations = PortalProvisionInvariants.Evaluate(
            [], [], [approvedSeamClaim, ambiguousSeamClaim, normalSubmitted, foreignApproved],
            new SeamRefundExemptions(
                [],
                [],
                ["spf-r2-seam-duplicate-claim-approved", "spf-r2-seam-duplicate-claim-ambiguous"]));

        // Fail-safe default: a claim WITHOUT an idempotency key can never match.
        var keyless = new FixtureClaimSnapshot(
            Guid.NewGuid(), DuplicateChargeClaimState.Approved,
            DuplicateChargeExecutionState.AwaitingExecution, null);
        IReadOnlyList<string> keylessViolations = PortalProvisionInvariants.Evaluate(
            [], [], [keyless],
            new SeamRefundExemptions(
                [],
                [],
                ["spf-r2-seam-duplicate-claim-approved", "spf-r2-seam-duplicate-claim-ambiguous"]));
        Assert.Equal(2, keylessViolations.Count);

        // The two seam claims are excused under --seam-deployed; the normal
        // submitted claim generates nothing and the foreign-key Approved claim
        // is still flagged for both its rules.
        Assert.Equal(2, violations.Count);
        Assert.Contains(
            violations,
            message => message.Contains("execution state AwaitingExecution", StringComparison.Ordinal));
    }

    // ----------------------------------------------------------------------
    // Seam duplicate-claim RERUN gate (regression: an unconditional
    // Approved/AwaitingExecution Require rejected an existing Approved/Ambiguous
    // ambiguous-group claim on the provisioner's SECOND run, before the skip
    // guard could run). The gate must permit EXACTLY the scripted states,
    // SKIP the prior run's executed ambiguous claim, and refuse anything else.
    // This gate is the code SeamClaimAsync actually branches on.
    // ----------------------------------------------------------------------

    [Fact]
    public void Seam_Claim_Rerun_Gate_Skips_The_Prior_Run_Ambiguous_Claim_And_Refuses_Drifted_States()
    {
        // The regression itself: a prior run left G4 at Approved/Ambiguous.
        // The second run MUST skip re-execution (SkipAlreadyExecuted) instead
        // of refusing the claim...
        Assert.Equal(
            SeamClaimRerunDisposition.SkipAlreadyExecutedByPriorRun,
            SeamClaimRerun.Resolve(
                approved: false,
                DuplicateChargeClaimState.Approved,
                DuplicateChargeExecutionState.Ambiguous));

        // ...while the approved group's identical readback (it must hold
        // Approved/AwaitingExecution forever) is NEVER accepted as executed.
        Assert.Null(SeamClaimRerun.Resolve(
            approved: true,
            DuplicateChargeClaimState.Approved,
            DuplicateChargeExecutionState.Ambiguous));

        // Fresh ambiguous state routes to the scripted execution; the fresh
        // approved state is the hold; anything unscripted is refused.
        Assert.Equal(
            SeamClaimRerunDisposition.ExecuteScriptedAmbiguous,
            SeamClaimRerun.Resolve(
                approved: false,
                DuplicateChargeClaimState.Approved,
                DuplicateChargeExecutionState.AwaitingExecution));
        Assert.Equal(
            SeamClaimRerunDisposition.HoldAsApprovedAwaitingExecution,
            SeamClaimRerun.Resolve(
                approved: true,
                DuplicateChargeClaimState.Approved,
                DuplicateChargeExecutionState.AwaitingExecution));
        Assert.Null(SeamClaimRerun.Resolve(
            approved: false,
            DuplicateChargeClaimState.Submitted,
            DuplicateChargeExecutionState.AwaitingExecution));
        Assert.Null(SeamClaimRerun.Resolve(
            approved: false,
            DuplicateChargeClaimState.Rejected,
            DuplicateChargeExecutionState.Ambiguous));
        Assert.Null(SeamClaimRerun.Resolve(
            approved: false,
            DuplicateChargeClaimState.Approved,
            DuplicateChargeExecutionState.Executing));
    }

    /// <summary>Offline Families Failed-refund builder with a caller-owned marker.</summary>
    private static Booking PaidCancelledFamiliesBooking(string marker)
    {
        Booking booking = Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(FixtureNow).AddDays(3),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "14 Synthetic Fixture Street, Alexandria (local test data)",
            marker,
            BookingPriceSnapshot.Calculate(500m, 15m),
            acceptanceDeadlineUtc: FixtureNow.AddHours(24),
            currentDate: DateOnly.FromDateTime(FixtureNow),
            createdOnUtc: FixtureNow);
        booking.RecordPaymentIntent("spf:r2-order", PaymentMethod.Card, FixtureNow.AddMinutes(-5));
        booking.MarkAsPaid("spf:r2-order", PortalProvisionIdentity.SeamCaregiverFailedTransactionId("r2"), FixtureNow.AddMinutes(-4));
        booking.CancelByFamily("Synthetic local fixture cancellation (no provider movement).", FixtureNow.AddMinutes(-3));
        return booking;
    }

    /// <summary>Offline builders for the persisted Families booking shapes.</summary>
    private static class PortalProvisionFixtureBookings
    {
        internal static Booking PaidCancelled()
        {
            Booking booking = Base();
            booking.RecordPaymentIntent("spf:r2-order", PaymentMethod.Card, FixtureNow.AddMinutes(-5));
            booking.MarkAsPaid("spf:r2-order", "spf:r2-txn", FixtureNow.AddMinutes(-4));
            booking.CancelByFamily("Synthetic local fixture cancellation (no provider movement).", FixtureNow.AddMinutes(-3));
            return booking;
        }

        internal static Booking ConfirmedAmbiguous()
        {
            Booking booking = Base();
            PortalProvisionDomainChecks.DrivePendingToConfirmed(
                booking, "spf:r2-order", "spf:r2-txn", FixtureNow);
            booking.ClaimRefundAttempt(Guid.NewGuid(), 100, FixtureNow.AddMinutes(-20));
            booking.MarkRefundOutcomeAmbiguous(
                "spf:r2-synthetic-ambiguous-ref",
                "Synthetic local ambiguity marker: no provider was contacted.",
                FixtureNow.AddMinutes(-19),
                100);
            return booking;
        }

        private static Booking Base() => Booking.Create(
            FamilyId.New(),
            UserId.New(),
            ElderlyId.New(),
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(FixtureNow).AddDays(3),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "14 Synthetic Fixture Street, Alexandria (local test data)",
            "spf:r2:caregiver-refund-ambiguity",
            BookingPriceSnapshot.Calculate(500m, 15m),
            acceptanceDeadlineUtc: FixtureNow.AddHours(24),
            currentDate: DateOnly.FromDateTime(FixtureNow),
            createdOnUtc: FixtureNow);
    }

    private static List<string> RequiredHeadPairs() =>
    [
        "care_homes=20261022213000_AddCareHomeRefundPolicySnapshots",
        "caregivers=20261021130000_AddCaregiverReviewNotes",
        "chat=20261013171755_AddAttachmentCleanupIndex",
        "cms=20261023114500_AddLibraryContentVideoStorageKey",
        "community=20261023150000_AddPostDeletionAudit",
        "families=20261023131500_AddCareRecords",
        "finance=20261024133842_AddDuplicateChargeClaims",
        "identity=20261022094500_AddPartnerContactOptionalFields",
        "notifications=20261010010618_AddEmailHtmlSnapshots"
    ];
}
