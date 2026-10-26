using System.Globalization;
using System.Text;
using System.Text.Json;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.PortalProvisioner;

namespace Sanad.PortalProvisionerTests;

/// <summary>
/// Focused facts for the demo corpus (D1): the 22-group classification, plan
/// determinism, the guard's two demo target kinds, the seam allowlist, and the
/// media workflow — including that the media files are written by the REAL
/// storage implementation and that a missing source fails the run. All
/// offline: no database, no network, no provider.
/// </summary>
public sealed class DemoCorpusFacts
{
    private static readonly DateTime Instant = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    // ----------------------------------------------------------------------
    // Classification
    // ----------------------------------------------------------------------

    [Fact]
    public void Classification_Is_Stable_Over_The_Twenty_Two_Group_Inventory()
    {
        Assert.Equal(22, DemoCorpusGroups.All.Count);
        Assert.Equal(
            new[]
            {
                "identity-accounts", "families-people", "families-subscriptions", "caregivers-profiles",
                "caregiver-ratings", "care-home-facilities", "care-home-inventory", "care-home-visits",
                "care-home-bookings", "caregiver-bookings", "finance-platform", "finance-claims",
                "finance-complaints", "chat", "community", "cms-library", "cms-wellness", "cms-faq",
                "cms-landing", "cms-legal", "cms-support", "notifications"
            },
            DemoCorpusGroups.All);

        // Execution order is the same set, in dependency order (it reorders
        // three pairs: caregiver-bookings before caregiver-ratings, cms-faq and
        // caregiver-ratings before cms-landing, families-subscriptions before
        // finance-platform).
        Assert.Equal(
            [.. DemoCorpusGroups.All.Order(StringComparer.Ordinal)],
            [.. DemoCorpusGroups.ExecutionOrder.Order(StringComparer.Ordinal)]);
        Assert.Equal(22, DemoCorpusGroups.ExecutionOrder.Count);
        Assert.NotEqual(DemoCorpusGroups.All, DemoCorpusGroups.ExecutionOrder);
        List<string> order = [.. DemoCorpusGroups.ExecutionOrder];
        Assert.True(order.IndexOf(DemoCorpusGroups.CaregiverBookings) < order.IndexOf(DemoCorpusGroups.CaregiverRatings));
        Assert.True(order.IndexOf(DemoCorpusGroups.CmsFaq) < order.IndexOf(DemoCorpusGroups.CmsLanding));
        Assert.True(order.IndexOf(DemoCorpusGroups.CmsWellness) < order.IndexOf(DemoCorpusGroups.CmsLanding));
        Assert.True(
            order.IndexOf(DemoCorpusGroups.FamiliesSubscriptions) < order.IndexOf(DemoCorpusGroups.FinancePlatform));

        // Exactly three groups persist a provider-call-armed end state and are
        // therefore seam-gated; the other nineteen are data-only.
        Assert.Equal(
            new[] { "care-home-bookings", "caregiver-bookings", "finance-claims" },
            DemoCorpusGroups.SeamGated);
        Assert.Equal(19, DemoCorpusGroups.DataOnly.Count);
        Assert.Empty(DemoCorpusGroups.DataOnly.Intersect(DemoCorpusGroups.SeamGated, StringComparer.Ordinal));

        // Every group is planned from exactly one counts table and one
        // description table: no group may be missing its approved depth line.
        foreach (string group in DemoCorpusGroups.All)
        {
            Assert.True(DemoCorpusGroups.IsDemoGroup(group));
            Assert.False(DemoCorpusGroups.IsReportFixtureGroup(group));
            Assert.True(DemoCorpusGroups.ExpectedCounts.ContainsKey(group), $"{group} has no approved count line");
            Assert.True(DemoCorpusGroups.Description.ContainsKey(group), $"{group} has no description line");
        }
    }

    [Fact]
    public void Classification_Separates_The_Demo_Groups_From_The_Report_Fixture_Groups()
    {
        // A demo group is never an S3a fixture group and vice versa: the two
        // corpora share no id, so one run can never expand the other's tables.
        foreach (string fixture in PortalProvisionGroups.DataOnlyGroups.Concat(PortalProvisionGroups.SeamGroups))
        {
            Assert.False(DemoCorpusGroups.IsDemoGroup(fixture), $"'{fixture}' is both a fixture and a demo group");
            Assert.True(DemoCorpusGroups.IsReportFixtureGroup(fixture));
        }

        Assert.False(DemoCorpusGroups.IsDemoGroup("not-a-group"));
        Assert.Equal("demo", DemoCorpusIdentity.MarkerPrefix);
        Assert.NotEqual("spf", DemoCorpusIdentity.MarkerPrefix);
        Assert.StartsWith("demo:demo1:", DemoCorpusIdentity.GroupMarker("demo1", DemoCorpusGroups.Chat), StringComparison.Ordinal);
        Assert.NotEqual(PortalProvisionIdentity.GroupMarker("demo1", PortalProvisionGroups.FamiliesCareRecords),
            DemoCorpusIdentity.GroupMarker("demo1", DemoCorpusGroups.FamiliesPeople));
    }

    // ----------------------------------------------------------------------
    // Plan
    // ----------------------------------------------------------------------

    [Fact]
    public void Plan_Is_ByteIdentical_And_Changes_With_Instant_Tag_And_Revision()
    {
        string Build(string tag, DateTime instant, string revision) =>
            JsonSerializer.Serialize(
                DemoCorpusPlan.Build(
                    tag, instant, revision, "demo-nonce", "C:/tmp/storage", DemoCorpusGroups.All, seamDeployed: true,
                    localSanadDbOverride: false),
                DemoCorpusPlan.JsonOptions);

        string baseline = Build("demo1", Instant, "rev");

        Assert.Equal(baseline, Build("demo1", Instant, "rev"));
        Assert.Contains("\"markerPrefix\": \"demo\"", baseline);
        Assert.Contains("\"localSanadDbOverride\": false", baseline);

        Assert.NotEqual(baseline, Build("demo1", Instant.AddMinutes(1), "rev"));
        Assert.NotEqual(baseline, Build("demo2", Instant, "rev"));
        Assert.NotEqual(baseline, Build("demo1", Instant, "other"));
    }

    [Fact]
    public void Plan_Plans_Every_Group_And_Gates_The_Three_Seam_Groups()
    {
        // Without the operator's declaration the three seam groups are REFUSED
        // by name and none is planned; the other nineteen still plan.
        DemoCorpusPlanReport undeclared = DemoCorpusPlan.Build(
            "demo1", Instant, "rev", "demo-nonce", "C:/tmp/storage", DemoCorpusGroups.All, seamDeployed: false,
            localSanadDbOverride: false);

        Assert.Equal(19, undeclared.PlannedGroups.Count);
        foreach (string seam in DemoCorpusGroups.SeamGated)
        {
            Assert.Contains(undeclared.RefusedGroups, message => message.StartsWith(seam, StringComparison.Ordinal));
            Assert.DoesNotContain(seam, undeclared.PlannedGroups.Select(group => group.Group));
        }

        // With the declaration every one of the 22 plans, in dependency order,
        // and nothing is refused.
        DemoCorpusPlanReport declared = DemoCorpusPlan.Build(
            "demo1", Instant, "rev", "demo-nonce", "C:/tmp/storage", DemoCorpusGroups.All, seamDeployed: true,
            localSanadDbOverride: false);

        Assert.Empty(declared.RefusedGroups);
        Assert.Equal(DemoCorpusGroups.ExecutionOrder, declared.PlannedGroups.Select(group => group.Group));
        Assert.All(declared.PlannedGroups, group => Assert.True(group.Planned));
        // The seam-gated groups plan in EXECUTION order (caregiver-bookings is
        // ordered before care-home-bookings).
        Assert.Equal(
            [DemoCorpusGroups.CaregiverBookings, DemoCorpusGroups.CareHomeBookings, DemoCorpusGroups.FinanceClaims],
            declared.PlannedGroups.Where(group => group.SeamGated).Select(group => group.Group));
        Assert.Equal(DemoCorpusGroups.ExecutionOrder, declared.ExecutionOrder);
        Assert.NotNull(declared.StorageRoot);
    }

    [Fact]
    public void Plan_Refuses_An_Unknown_Group_And_The_Fixture_Groups_Under_The_Local_Override()
    {
        DemoCorpusPlanReport unknown = DemoCorpusPlan.Build(
            "demo1", Instant, "rev", "demo-nonce", "C:/tmp/storage", ["not-a-group"], seamDeployed: false,
            localSanadDbOverride: false);
        Assert.Empty(unknown.PlannedGroups);
        Assert.Contains(unknown.RefusedGroups, message => message.Contains("not a demo-corpus group", StringComparison.Ordinal));

        // The single recorded local override writes the demo corpus only.
        DemoCorpusPlanReport overrideAttempt = DemoCorpusPlan.Build(
            "demo1", Instant, "rev", "local-demo", "C:/tmp/storage",
            [DemoCorpusGroups.IdentityAccounts, PortalProvisionGroups.FamiliesCareRecords],
            seamDeployed: false,
            localSanadDbOverride: true);
        Assert.Equal([DemoCorpusGroups.IdentityAccounts], overrideAttempt.PlannedGroups.Select(group => group.Group));
        Assert.Contains(
            overrideAttempt.RefusedGroups,
            message => message.Contains("report-fixture group was requested", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_Logins_Separate_The_Demo_Namespace_And_Carry_No_Secret()
    {
        DemoCorpusPlanReport plan = DemoCorpusPlan.Build(
            "demo1", Instant, "rev", "demo-nonce", "C:/tmp/storage", DemoCorpusGroups.All, seamDeployed: true,
            localSanadDbOverride: false);

        Assert.Equal(16, plan.PlannedLogins.Count);
        Assert.Equal(DemoCorpusPlan.LoginRoles, plan.PlannedLogins.Select(login => login.Role));
        Assert.All(plan.PlannedLogins, login =>
        {
            Assert.Equal($"[demo1:{login.Role}]", login.Marker);
            Assert.StartsWith("+2010", login.Phone, StringComparison.Ordinal);
        });
        Assert.Equal("demo1.family.owner@test.sanad.local", plan.PlannedLogins[0].Email);

        // The address SHAPE is shared with the S3a fixture identity; the
        // separation is the pinned run tag (fixture 'r2', demo 'demo1') and the
        // structurally separate demo phone namespace (demo ordinals start at 51).
        Assert.Equal("r2.family.owner@test.sanad.local", PortalProvisionIdentity.FixtureEmail("r2", "family.owner"));
        Assert.NotEqual(PortalProvisionIdentity.FixtureEmail("r2", "family.owner"), plan.PlannedLogins[0].Email);
        Assert.NotEqual(
            PortalProvisionIdentity.DeterministicPhone("demo1", "family.owner"),
            DemoCorpusIdentity.Phone("demo1", "family.owner"));

        // The elderly identities are phone-keyed, so no address is planned.
        Assert.Equal(
            2,
            plan.PlannedLogins.Count(login => login.Email.StartsWith("<none", StringComparison.Ordinal)));

        string json = JsonSerializer.Serialize(plan, DemoCorpusPlan.JsonOptions);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------------------
    // Seam allowlist
    // ----------------------------------------------------------------------

    [Fact]
    public void Seam_Allowlist_Is_Exact_Deterministic_And_Matches_The_Persisted_Ids()
    {
        DemoCorpusPlanReport plan = DemoCorpusPlan.Build(
            "demo1", Instant, "rev", "demo-nonce", "C:/tmp/storage", DemoCorpusGroups.All, seamDeployed: true,
            localSanadDbOverride: false);

        // A care-home Paymob transaction id is a long, so its allowlist key is
        // the numeric StableNumber of the marker.
        string rejectedPending = DemoCorpusIdentity.CareHomeRefundTransactionId("demo1", "rejected-refund-pending");
        string failedCareHome = DemoCorpusIdentity.CareHomeRefundTransactionId("demo1", "seam-failed-refund");
        Assert.True(long.TryParse(rejectedPending, NumberStyles.None, CultureInfo.InvariantCulture, out long numeric));
        Assert.True(numeric > 0);
        Assert.Equal(
            PortalProvisionIdentity.StableNumber("demo:demo1:care-home-bookings:rejected-refund-pending")
                .ToString(CultureInfo.InvariantCulture),
            rejectedPending);

        // A caregiver/claim transaction id is a marker-derived string made
        // colon-free: .NET configuration splits keys on ':' and the seam
        // allowlist IS configuration, so a ':' in the id would be silently
        // dropped by the binder and that entry could never be armed.
        Assert.Equal(
            "demo-demo1-caregiver-bookings-seam-failed-refund-order-txn",
            DemoCorpusIdentity.CaregiverRefundTransactionId("demo1", "seam-failed-refund"));
        Assert.Equal(
            "demo-demo1-finance-claims-ambiguous-capture",
            DemoCorpusIdentity.ClaimCaptureTransactionId("demo1", "ambiguous"));
        Assert.Equal(
            "demo:demo1:finance-claims:ambiguous:claim",
            DemoCorpusIdentity.ClaimIdempotencyKey("demo1", "ambiguous"));

        Assert.Equal(
            new[]
            {
                "caregiver-bookings [seam-failed-refund]: FixtureRefundSeam:RefundOutcomes"
                    + "['demo-demo1-caregiver-bookings-seam-failed-refund-order-txn']='Rejected'",
                $"care-home-bookings [rejected-refund-pending]: FixtureRefundSeam:RefundOutcomes['{rejectedPending}']='Rejected'",
                $"care-home-bookings [seam-failed-refund]: FixtureRefundSeam:RefundOutcomes['{failedCareHome}']='Rejected'",
                "finance-claims [submitted]: FixtureRefundSeam:RefundOutcomes"
                    + "['demo-demo1-finance-claims-submitted-capture']='Rejected'",
                "finance-claims [approved]: FixtureRefundSeam:RefundOutcomes"
                    + "['demo-demo1-finance-claims-approved-capture']='Rejected'",
                "finance-claims [ambiguous]: FixtureRefundSeam:RefundOutcomes"
                    + "['demo-demo1-finance-claims-ambiguous-capture']='Unknown'",
                "finance-claims [failed]: FixtureRefundSeam:RefundOutcomes"
                    + "['demo-demo1-finance-claims-failed-capture']='Rejected'"
            },
            plan.SeamAllowlist);

        // Every allowlist KEY must be colon-free, or .NET configuration drops
        // the entry and the deployed revision can never arm it.
        foreach (string entry in plan.SeamAllowlist)
        {
            string key = entry[
                (entry.IndexOf("['", StringComparison.Ordinal) + 2)..entry.IndexOf("']", StringComparison.Ordinal)];
            Assert.DoesNotContain(':', key);
        }

        // Deterministic for identical inputs, and the print is derived from the
        // REQUESTED set: a plan that refuses the seam groups still tells the
        // operator exactly what to allowlist before re-running.
        Assert.Equal(
            plan.SeamAllowlist,
            DemoCorpusPlan.DescribeSeamAllowlist("demo1", DemoCorpusGroups.All));
        Assert.Equal(
            plan.SeamAllowlist,
            DemoCorpusPlan.DescribeSeamAllowlist("demo1", [DemoCorpusGroups.CareHomeBookings, DemoCorpusGroups.CaregiverBookings, DemoCorpusGroups.FinanceClaims]));

        Assert.Equal(
            ["no seam-gated group requested: the deployed revision needs no FixtureRefundSeam allowlist entry"],
            DemoCorpusPlan.DescribeSeamAllowlist("demo1", [DemoCorpusGroups.IdentityAccounts]));
    }

    [Fact]
    public void Seam_Armed_Caregiver_Slot_Refuses_When_The_Persisted_Id_Does_Not_Match_The_Allowlist()
    {
        // The configured (colon-free) ids the plan prints and the allowlist entry carries.
        string configuredOrder = DemoCorpusIdentity.CaregiverOrderId("demo1", "seam-failed-refund");
        string configuredTransaction = DemoCorpusIdentity.CaregiverRefundTransactionId("demo1", "seam-failed-refund");
        string legacyTransaction = "demo:demo1:caregiver-bookings:seam-failed-refund:order:txn";
        string legacyOrder = "demo:demo1:caregiver-bookings:seam-failed-refund:order";

        // The pre-fix target's exact legacy shape: the booking row was already
        // slugged but the SUCCEEDED payment transaction (the one the admin refund
        // retry selects and sends) still carries the colon-form id. Payment
        // SUCCESS is the retry's eligibility state, not refund terminality, so
        // this slot is live for a retry — and the seam would NOT intercept it.
        string? mismatch = DemoCorpusIdentity.SeamArmedIdMismatch(
            "seam-failed-refund",
            configuredOrder,
            configuredTransaction,
            persistedOrderId: legacyOrder,
            persistedTransactionId: legacyTransaction,
            persistedBookingTransactionId: configuredTransaction);
        Assert.NotNull(mismatch);
        Assert.Contains("NOT seam-protected", mismatch, StringComparison.Ordinal);
        Assert.Contains(legacyTransaction, mismatch, StringComparison.Ordinal);
        Assert.Contains(configuredTransaction, mismatch, StringComparison.Ordinal);

        // No succeeded transaction persisted at all: the armed state exists with
        // nothing the configured entry could ever match.
        mismatch = DemoCorpusIdentity.SeamArmedIdMismatch(
            "seam-failed-refund", configuredOrder, configuredTransaction,
            persistedOrderId: null, persistedTransactionId: null, persistedBookingTransactionId: configuredTransaction);
        Assert.NotNull(mismatch);
        Assert.Contains("NOT seam-protected", mismatch, StringComparison.Ordinal);

        // Transaction id matches but the order locator is inconsistent: refuse too.
        mismatch = DemoCorpusIdentity.SeamArmedIdMismatch(
            "seam-failed-refund", configuredOrder, configuredTransaction,
            persistedOrderId: legacyOrder, persistedTransactionId: configuredTransaction,
            persistedBookingTransactionId: configuredTransaction);
        Assert.NotNull(mismatch);
        Assert.Contains("NOT seam-protected", mismatch, StringComparison.Ordinal);

        // The fallback locator (the booking's own id) mismatching is also a refusal.
        mismatch = DemoCorpusIdentity.SeamArmedIdMismatch(
            "seam-failed-refund", configuredOrder, configuredTransaction,
            persistedOrderId: configuredOrder, persistedTransactionId: configuredTransaction,
            persistedBookingTransactionId: legacyTransaction);
        Assert.NotNull(mismatch);
        Assert.Contains("NOT seam-protected", mismatch, StringComparison.Ordinal);

        // The repaired, fully consistent shape (both targets now) is accepted as
        // seam-protected: null means the slot may be reported as covered.
        Assert.Null(DemoCorpusIdentity.SeamArmedIdMismatch(
            "seam-failed-refund", configuredOrder, configuredTransaction,
            persistedOrderId: configuredOrder, persistedTransactionId: configuredTransaction,
            persistedBookingTransactionId: configuredTransaction));
    }

    [Fact]
    public void Complaint_Refund_Cases_Are_Deterministic_Unique_And_Not_Capture_Derived()
    {
        Guid[] cases =
        [
            DemoCorpusIdentity.ComplaintRefundCaseId("demo1", "received"),
            DemoCorpusIdentity.ComplaintRefundCaseId("demo1", "investigating"),
            DemoCorpusIdentity.ComplaintRefundCaseId("demo1", "resolved"),
            DemoCorpusIdentity.ComplaintRefundCaseId("demo1", "refused")
        ];

        Assert.Equal(4, cases.Distinct().Count());
        Assert.All(cases, value => Assert.NotEqual(Guid.Empty, value));

        // Stable across calls and derived only from the demo marker, so it can
        // never equal a capture-derived duplicate-charge case.
        Assert.Equal(cases[0], DemoCorpusIdentity.ComplaintRefundCaseId("demo1", "received"));
        Assert.NotEqual(cases[0], DemoCorpusIdentity.ComplaintRefundCaseId("demo2", "received"));
        Assert.DoesNotContain(
            Sanad.Modules.Finance.Application.SubscriptionRefundCaseIds.ForDuplicateCharge(cases[0]),
            cases);
    }

    // ----------------------------------------------------------------------
    // Target guard: the two demo kinds
    // ----------------------------------------------------------------------

    [Fact]
    public void Guard_Parse_Accepts_A_Demo_Nonce_Loopback_Target_And_Refuses_Everything_Else()
    {
        // The accepted shape: a loopback host and a generated nonce name.
        PortalProvisionExpectations nonce = ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDemoNonce_20261025120000", "rev", "demo-nonce",
            tunnelDeclared: false, RequiredHeadPairs(), out IReadOnlyList<string> refusals);
        Assert.Empty(refusals);
        Assert.True(nonce.IsDemoTarget);
        Assert.Equal("demo-nonce", nonce.TargetKind);

        // A non-loopback host is refused: the nonce target is a local dry run.
        ProvisionerTargetGuard.Parse(
            "10.0.0.9", "5433", "SanadDemoNonce_20261025120000", "rev", "demo-nonce",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals);
        Assert.Contains(refusals, message => message.Contains("must be loopback", StringComparison.Ordinal));

        // A name that is not a generated nonce is refused.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadScratch", "rev", "demo-nonce",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals);
        Assert.Contains(
            refusals,
            message => message.Contains("requires a generated nonce database name", StringComparison.Ordinal));

        // The protected local database is refused TWICE over: once as a
        // protected name and once as a non-nonce name.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDb", "rev", "demo-nonce",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals);
        Assert.Contains(refusals, message => message.Contains("protected local database", StringComparison.Ordinal));
        Assert.Contains(
            refusals,
            message => message.Contains("requires a generated nonce database name", StringComparison.Ordinal));

        // A tunnel declaration contradicts a local dry run and is refused.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDemoNonce_20261025120000", "rev", "demo-nonce",
            tunnelDeclared: true, RequiredHeadPairs(), out refusals);
        Assert.Contains(
            refusals,
            message => message.Contains("--tunnel-declared is refused", StringComparison.Ordinal));
    }

    [Fact]
    public void Guard_Parse_Accepts_The_Local_SanadDb_Only_With_The_Recorded_Override()
    {
        // The accepted shape: the exact protected name, loopback, tag demo1 and
        // the explicit --allow-local-sanad-db declaration.
        PortalProvisionExpectations local = ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDb", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out IReadOnlyList<string> refusals,
            allowLocalSanadDb: true, runTag: "demo1");
        Assert.Empty(refusals);
        Assert.True(local.IsDemoTarget);
        Assert.True(local.AllowLocalSanadDb);
        Assert.Equal("demo1", local.RunTag);

        // The override is never implied.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDb", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: false, runTag: "demo1");
        Assert.Contains(
            refusals,
            message => message.Contains("without --allow-local-sanad-db", StringComparison.Ordinal));

        // It applies to SanadDb and to nothing else.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadIntegrationDb", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo1");
        Assert.Contains(
            refusals,
            message => message.Contains("applies only to the local database 'SanadDb'", StringComparison.Ordinal));

        // And only under the one recorded run tag.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDb", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo2");
        Assert.Contains(refusals, message => message.Contains("--run-tag 'demo1'", StringComparison.Ordinal));

        // It is a DIRECT local connection: a tunnel declaration is refused, so
        // the override can never be pointed at the rehearsal cluster.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDb", "rev", "local-demo",
            tunnelDeclared: true, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo1");
        Assert.Contains(refusals, message => message.Contains("DIRECT local connection", StringComparison.Ordinal));

        // Production stays refused outright, in every demo path.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDb", "rev", "production",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo1");
        Assert.Contains(refusals, message => message.Contains("Production is refused outright", StringComparison.Ordinal));
    }

    [Fact]
    public void Guard_Enforces_The_Local_Demo_Boundary_Unconditionally()
    {
        // Takeover finding #5: the local-demo constraints used to sit inside
        // the protected-server-name branch, so a DIFFERENT database name
        // bypassed every one of them. Each constraint must fire for ANY
        // declared database name.

        // 1. An alternative (non-protected) name with no override: refused for
        //    BOTH the name and the absent override — the boundary does not
        //    depend on the protected-name branch.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDemoRun", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out IReadOnlyList<string> refusals,
            allowLocalSanadDb: false, runTag: "demo1");
        Assert.Contains(
            refusals,
            message => message.Contains("applies only to the local database 'SanadDb'", StringComparison.Ordinal));
        Assert.Contains(refusals, message => message.Contains("without --allow-local-sanad-db", StringComparison.Ordinal));

        // 2. Even WITH the override and the right tag, an alternative name
        //    stays refused (and only for the name: the override was declared).
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDemoRun", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo1");
        Assert.Contains(
            refusals,
            message => message.Contains("applies only to the local database 'SanadDb'", StringComparison.Ordinal));
        Assert.DoesNotContain(refusals, message => message.Contains("without --allow-local-sanad-db", StringComparison.Ordinal));

        // 3. The name match is exact Ordinal: 'sanaddb' is not 'SanadDb'.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "sanaddb", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo1");
        Assert.Contains(
            refusals,
            message => message.Contains("applies only to the local database 'SanadDb'", StringComparison.Ordinal));

        // 4. The exact protected name without the override: still refused —
        //    the override is never implied even where the name IS protected.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDb", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: false, runTag: "demo1");
        Assert.Contains(refusals, message => message.Contains("without --allow-local-sanad-db", StringComparison.Ordinal));

        // 5. A wrong run tag, with everything else correct.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDb", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo2");
        Assert.Contains(
            refusals,
            message => message.Contains("is accepted only with --run-tag 'demo1' (got 'demo2')", StringComparison.Ordinal));

        // 6. An ABSENT run tag, with everything else correct.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDb", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: null);
        Assert.Contains(
            refusals,
            message => message.Contains("requires --run-tag 'demo1'", StringComparison.Ordinal));

        // 7. A tunnel declaration, with everything else correct.
        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDb", "rev", "local-demo",
            tunnelDeclared: true, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo1");
        Assert.Contains(
            refusals,
            message => message.Contains("--tunnel-declared is refused", StringComparison.Ordinal));

        // 8. A non-loopback host, with everything else correct.
        ProvisionerTargetGuard.Parse(
            "10.0.0.9", "5433", "SanadDb", "rev", "local-demo",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo1");
        Assert.Contains(refusals, message => message.Contains("must be loopback", StringComparison.Ordinal));

        // 9. The override flag declared under any OTHER target kind is itself
        //    refused, so it can never silently attach to rehearsal or nonce.
        ProvisionerTargetGuard.Parse(
            "72.62.92.144", "5432", "SanadDb", "rev", "rehearsal",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "r2");
        Assert.Contains(
            refusals,
            message => message.Contains(
                "accepted only with --target-kind local-demo (got 'rehearsal')", StringComparison.Ordinal));

        ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDemoNonce_20261025120000", "rev", "demo-nonce",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo1");
        Assert.Contains(
            refusals,
            message => message.Contains(
                "accepted only with --target-kind local-demo (got 'demo-nonce')", StringComparison.Ordinal));

        // 10. Positive controls: the exact recorded shape is accepted on all
        //     three loopback hosts; the rehearsal and demo-nonce semantics are
        //     untouched by the unconditional boundary (a tunneled rehearsal on
        //     the protected name still parses, a nonce still plans, and a
        //     rehearsal demo run under tag demo1 without the override).
        foreach (string host in new[] { "localhost", "127.0.0.1", "::1" })
        {
            PortalProvisionExpectations accepted = ProvisionerTargetGuard.Parse(
                host, "5433", "SanadDb", "rev", "local-demo",
                tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: true, runTag: "demo1");
            Assert.Empty(refusals);
            Assert.Equal("demo1", accepted.RunTag);
            Assert.True(accepted.AllowLocalSanadDb);
            Assert.True(accepted.IsDemoTarget);
        }

        ProvisionerTargetGuard.Parse(
            "localhost", "5432", "SanadDb", "rev", "rehearsal",
            tunnelDeclared: true, RequiredHeadPairs(), out refusals);
        Assert.Empty(refusals);

        ProvisionerTargetGuard.Parse(
            "72.62.92.144", "5432", "SanadDb", "rev", "rehearsal",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: false, runTag: "demo1");
        Assert.Empty(refusals);

        PortalProvisionExpectations nonce = ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDemoNonce_20261025120000", "rev", "demo-nonce",
            tunnelDeclared: false, RequiredHeadPairs(), out refusals, allowLocalSanadDb: false, runTag: "demo1");
        Assert.Empty(refusals);
        Assert.False(nonce.AllowLocalSanadDb);
    }

    [Fact]
    public void Guard_Identity_Pins_A_Demo_Target_To_Loopback_And_The_Cluster_Identifier()
    {
        PortalProvisionExpectations nonce = ProvisionerTargetGuard.Parse(
            "localhost", "5433", "SanadDemoNonce_20261025120000", "rev", "demo-nonce",
            tunnelDeclared: false, RequiredHeadPairs(), out _);

        ProvisionerTargetGuard.PortalReachedIdentity loopback =
            new("SanadDemoNonce_20261025120000", "127.0.0.1", 5433, "7246867520585489674", true, null);

        // Accepted only when the reached address is loopback AND the cluster
        // identifier matches the pinned value.
        Assert.Empty(ProvisionerTargetGuard.EvaluateReachedIdentity(nonce, "7246867520585489674", loopback));

        // A name that resolves off-box is refused even though the declared host
        // was loopback: the demo kinds are local dry runs.
        IReadOnlyList<string> remote = ProvisionerTargetGuard.EvaluateReachedIdentity(
            nonce, "7246867520585489674", loopback with { ServerAddress = "203.0.113.7" });
        Assert.Contains(remote, message => message.Contains("local dry-run target", StringComparison.Ordinal));

        // The cluster identity is mandatory: a demo target on a different
        // cluster (e.g. the wrong local database) is refused without printing
        // either identifier.
        IReadOnlyList<string> mismatched = ProvisionerTargetGuard.EvaluateReachedIdentity(
            nonce, "7246867520585489674", loopback with { SystemIdentifier = "1111111111111111111" });
        Assert.Contains(
            mismatched,
            message => message.Contains("cluster system identifier mismatch", StringComparison.Ordinal));
        Assert.DoesNotContain(mismatched, message => message.Contains("1111111111111111111", StringComparison.Ordinal));

        IReadOnlyList<string> unreadable = ProvisionerTargetGuard.EvaluateReachedIdentity(
            nonce, "7246867520585489674", loopback with { SystemIdentifierReadable = false });
        Assert.Contains(
            unreadable,
            message => message.Contains("cannot read pg_control_system()", StringComparison.Ordinal));

        IReadOnlyList<string> unpinned = ProvisionerTargetGuard.EvaluateReachedIdentity(nonce, null, loopback);
        Assert.Contains(unpinned, message => message.Contains("cluster system identifier is mandatory", StringComparison.Ordinal));
    }

    // ----------------------------------------------------------------------
    // Media workflow (real storage implementation, offline temp root)
    // ----------------------------------------------------------------------

    [Fact]
    public async Task Media_Missing_Source_Fails_The_Run_Before_Any_Write()
    {
        using var scope = new TempMediaScope();
        DemoCorpusMedia media = new(scope.StorageRoot, scope.MediaDirectory);

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => media.ImagePublicKeyAsync("landing"));
        Assert.Contains("is missing from the media directory", failure.Message, StringComparison.Ordinal);
        Assert.Contains("New-DemoMedia.ps1", failure.Message, StringComparison.Ordinal);

        // Nothing was written into the upload root by the refused call.
        Assert.False(Directory.Exists(Path.Combine(scope.StorageRoot, "landing")));
    }

    [Fact]
    public async Task Media_Is_Written_By_The_Real_Storage_Implementation_And_Reused_Everywhere()
    {
        using var scope = new TempMediaScope();
        scope.WriteSources();
        DemoCorpusMedia media = new(scope.StorageRoot, scope.MediaDirectory);

        // Public image: written by the real SaveAsync into the folder the real
        // uploader uses, and the returned key resolves under the upload root.
        string publicKey = await media.ImagePublicKeyAsync("landing");
        Assert.False(publicKey.StartsWith("private/", StringComparison.Ordinal));
        Assert.StartsWith("landing/", publicKey, StringComparison.Ordinal);
        Assert.EndsWith(".jpg", publicKey, StringComparison.Ordinal);
        Assert.True(File.Exists(scope.PublicPath(publicKey)));

        // ONE generated file is reused: a second field returns the same key
        // rather than copying a second image.
        Assert.Equal(publicKey, await media.ImagePublicKeyAsync("community-media"));

        // The abstraction's only read is the private reader, which is exactly
        // why the public key is confirmed on disk above.
        Result<Sanad.BuildingBlocks.Application.Abstractions.Storage.PrivateFileContent> publicRead =
            await media.Storage.OpenReadAsync(publicKey);
        Assert.True(publicRead.IsFailure);

        // Private image (care-home media is domain-enforced private).
        string privateImage = await media.ImagePrivateKeyAsync("care-home-media", maxBytes: 5_242_880);
        Assert.StartsWith("private/care-home-media/", privateImage, StringComparison.Ordinal);
        await AssertContentAsync(media, privateImage, "image/jpeg", scope.ImageBytes.Length);

        // The document.
        string document = await media.DocumentKeyAsync("care-home-documents");
        Assert.StartsWith("private/care-home-documents/", document, StringComparison.Ordinal);
        Assert.EndsWith(".pdf", document, StringComparison.Ordinal);
        await AssertContentAsync(media, document, "application/pdf", scope.DocumentBytes.Length);
        Assert.Equal(scope.DocumentBytes.Length, media.DocumentLength);

        // The video: only the private writer accepts video/mp4 — the public
        // writer refuses it, which is why every video key is private.
        using (var buffer = new MemoryStream(scope.VideoBytes))
        {
            Result<Sanad.BuildingBlocks.Application.Abstractions.Storage.StoredFile> refused =
                await media.Storage.SaveAsync(buffer, "video/mp4", scope.VideoBytes.Length, "landing");
            Assert.True(refused.IsFailure);
        }

        string video = await media.VideoKeyAsync("chat-attachments");
        Assert.StartsWith("private/chat-attachments/", video, StringComparison.Ordinal);
        Assert.EndsWith(".mp4", video, StringComparison.Ordinal);
        await AssertContentAsync(media, video, "video/mp4", scope.VideoBytes.Length);
    }

    [Fact]
    public async Task Media_Honours_The_Private_Size_Cap_Without_Bypassing_The_Writer()
    {
        using var scope = new TempMediaScope();
        scope.WriteSources();
        DemoCorpusMedia media = new(scope.StorageRoot, scope.MediaDirectory);

        // The private writer's per-file cap is honoured through the abstraction,
        // not sidestepped: a source above the cap is refused by the writer.
        var refused = await media.Storage.SavePrivateAsync(
            new MemoryStream(scope.ImageBytes), "image/jpeg", scope.ImageBytes.Length, "care-home-media", maximumBytes: 8);
        Assert.True(refused.IsFailure);
        Assert.Equal("Storage.File.TooLarge", refused.Error.Code);
    }

    [Fact]
    public async Task Media_Replay_Reuses_The_Sidecar_Keys_And_Produces_Zero_New_Files()
    {
        using var scope = new TempMediaScope();
        scope.WriteSources();
        string privateRoot = scope.StorageRoot + "-private";
        string[] privateFolders = ["care-home-media", "chat-attachments"];
        foreach (string folder in privateFolders)
        {
            Directory.CreateDirectory(Path.Combine(privateRoot, folder));
        }

        long CountFiles() =>
            Directory.EnumerateFiles(scope.StorageRoot, "*", SearchOption.AllDirectories).Count()
                + Directory.EnumerateFiles(privateRoot, "*", SearchOption.AllDirectories).Count();

        DemoCorpusMedia first = new(scope.StorageRoot, scope.MediaDirectory);
        string public1 = await first.ImagePublicKeyAsync("landing");
        string private1 = await first.ImagePrivateKeyAsync("care-home-media", maxBytes: 5_242_880);
        string document1 = await first.DocumentKeyAsync("care-home-documents");
        string video1 = await first.VideoKeyAsync("chat-attachments");
        long afterFirstRun = CountFiles();

        // A replay is a NEW process: fresh caches, same media directory and the
        // same upload root. The sidecar must resurrect the produced keys and the
        // upload root must not gain a single new file.
        DemoCorpusMedia replay = new(scope.StorageRoot, scope.MediaDirectory);
        Assert.Equal(public1, await replay.ImagePublicKeyAsync("landing"));
        Assert.Equal(private1, await replay.ImagePrivateKeyAsync("care-home-media", maxBytes: 5_242_880));
        Assert.Equal(document1, await replay.DocumentKeyAsync("care-home-documents"));
        Assert.Equal(video1, await replay.VideoKeyAsync("chat-attachments"));
        Assert.Equal(afterFirstRun, CountFiles());

        // Reused keys still resolve to the real bytes through the abstraction.
        await AssertContentAsync(replay, private1, "image/jpeg", scope.ImageBytes.Length);
        await AssertContentAsync(replay, video1, "video/mp4", scope.VideoBytes.Length);
    }

    [Fact]
    public async Task Media_Replay_On_A_Fresh_Process_Populates_Length_Metadata()
    {
        using var scope = new TempMediaScope();
        scope.WriteSources();

        DemoCorpusMedia first = new(scope.StorageRoot, scope.MediaDirectory);
        await first.ImagePublicKeyAsync("landing");
        await first.ImagePrivateKeyAsync("care-home-media", maxBytes: 5_242_880);
        await first.DocumentKeyAsync("care-home-documents");
        Assert.Equal(scope.ImageBytes.Length, first.ImageLength);
        Assert.Equal(scope.DocumentBytes.Length, first.DocumentLength);

        // A replay is a fresh process: its per-instance cache is empty. The
        // sidecar reuse must repopulate the length metadata the rows carry —
        // leaving it at zero on the reuse path is exactly the regression this
        // hardening closes.
        DemoCorpusMedia replay = new(scope.StorageRoot, scope.MediaDirectory);
        await replay.ImagePublicKeyAsync("landing");
        Assert.Equal(scope.ImageBytes.Length, replay.ImageLength);

        await replay.ImagePrivateKeyAsync("care-home-media", maxBytes: 5_242_880);
        Assert.Equal(scope.ImageBytes.Length, replay.ImageLength);

        await replay.DocumentKeyAsync("care-home-documents");
        Assert.Equal(scope.DocumentBytes.Length, replay.DocumentLength);
    }

    [Fact]
    public async Task Media_Replay_With_A_Missing_Recorded_File_Refuses_And_Writes_No_Unreferenced_Replacement()
    {
        using var scope = new TempMediaScope();
        scope.WriteSources();

        DemoCorpusMedia first = new(scope.StorageRoot, scope.MediaDirectory);
        string public1 = await first.ImagePublicKeyAsync("landing");
        File.Delete(scope.PublicPath(public1));

        long filesBefore = Directory
            .EnumerateFiles(scope.StorageRoot, "*", SearchOption.AllDirectories).Count();

        // The sidecar names the key the demo rows already reference. A fresh
        // replacement file would leave those rows pointing at the broken key
        // while the upload root quietly gains an orphan write, so the run
        // refuses with the repair guidance instead.
        DemoCorpusMedia replay = new(scope.StorageRoot, scope.MediaDirectory);
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => replay.ImagePublicKeyAsync("landing"));
        Assert.Contains("failed real-storage validation", failure.Message, StringComparison.Ordinal);
        Assert.Contains("remove the sidecar 'produced-keys.json'", failure.Message, StringComparison.Ordinal);

        // No replacement file appeared: the upload root gained nothing.
        Assert.Equal(
            filesBefore,
            Directory.EnumerateFiles(scope.StorageRoot, "*", SearchOption.AllDirectories).Count());
    }

    [Fact]
    public async Task Media_Replay_With_An_Empty_Stored_File_Refuses()
    {
        using var scope = new TempMediaScope();
        scope.WriteSources();

        DemoCorpusMedia first = new(scope.StorageRoot, scope.MediaDirectory);
        string document = await first.DocumentKeyAsync("care-home-documents");

        // The stored file lives exactly where the writer composes it: the
        // '-private' sibling root with the FULL key appended.
        string storedPath = Path.Combine(
            scope.StorageRoot + "-private",
            document.Replace('/', Path.DirectorySeparatorChar));

        // Empty: the referenced file was truncated to zero bytes.
        using (File.Create(storedPath)) { }
        DemoCorpusMedia emptyReplay = new(scope.StorageRoot, scope.MediaDirectory);
        InvalidOperationException emptyFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => emptyReplay.DocumentKeyAsync("care-home-documents"));
        Assert.Contains("the stored file is empty", emptyFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Media_Replay_With_An_Altered_Stored_File_Refuses()
    {
        using var scope = new TempMediaScope();
        scope.WriteSources();

        DemoCorpusMedia first = new(scope.StorageRoot, scope.MediaDirectory);
        string document = await first.DocumentKeyAsync("care-home-documents");

        // Altered: bytes differ from the source media the writer persisted.
        // The stored file lives exactly where the writer composes it: the
        // '-private' sibling root with the FULL key appended.
        string storedPath = Path.Combine(
            scope.StorageRoot + "-private",
            document.Replace('/', Path.DirectorySeparatorChar));
        byte[] altered = Encoding.ASCII.GetBytes("%PDF-not-the-demo-document\n");
        File.WriteAllBytes(storedPath, altered);
        DemoCorpusMedia alteredReplay = new(scope.StorageRoot, scope.MediaDirectory);
        InvalidOperationException alteredFailure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => alteredReplay.DocumentKeyAsync("care-home-documents"));
        Assert.Contains("do not match source", alteredFailure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Media_Replay_With_A_Wrong_Type_Recorded_Key_Refuses()
    {
        using var scope = new TempMediaScope();
        scope.WriteSources();

        DemoCorpusMedia first = new(scope.StorageRoot, scope.MediaDirectory);
        string document1 = await first.DocumentKeyAsync("care-home-documents");
        string video1 = await first.VideoKeyAsync("chat-attachments");

        // Hand-crafted bad key/target mix within ONE target: the video slot
        // records the DOCUMENT's key. The reuse validation reads it through the
        // real abstraction, sees 'application/pdf' where the slot writes
        // 'video/mp4', and refuses instead of seeding a wrong-typed reference.
        string manifestPath = Path.Combine(scope.MediaDirectory, "produced-keys.json");
        string manifest = File.ReadAllText(manifestPath);
        Assert.Contains($"\"VideoKey\": \"{video1}\"", manifest, StringComparison.Ordinal);
        File.WriteAllText(
            manifestPath,
            manifest.Replace($"\"VideoKey\": \"{video1}\"", $"\"VideoKey\": \"{document1}\"", StringComparison.Ordinal));

        DemoCorpusMedia replay = new(scope.StorageRoot, scope.MediaDirectory);
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => replay.VideoKeyAsync("chat-attachments"));
        Assert.Contains("reports 'application/pdf' but 'video/mp4' was written", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Media_Sidecar_From_Another_Target_Is_Ignored_Not_Adopted()
    {
        using var scope = new TempMediaScope();
        scope.WriteSources();

        DemoCorpusMedia first = new(scope.StorageRoot, scope.MediaDirectory);
        string firstKey = await first.ImagePublicKeyAsync("landing");

        // A second target root sharing the same media directory: the sidecar's
        // scope fingerprint (target root) does not match, so its keys are
        // ignored — the second target establishes its own media instead of
        // seeding references to files that live under the OTHER root.
        string otherRoot = scope.StorageRoot + "-other-target";
        Directory.CreateDirectory(otherRoot);
        DemoCorpusMedia other = new(otherRoot, scope.MediaDirectory);
        string otherKey = await other.ImagePublicKeyAsync("landing");

        Assert.NotEqual(firstKey, otherKey);
        Assert.StartsWith("landing/", otherKey, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(otherRoot, otherKey.Replace('/', Path.DirectorySeparatorChar))));
        // The sidecar now names the second target's establishment; the first
        // key remains untouched under the first root.
        Assert.True(File.Exists(scope.PublicPath(firstKey)));
    }

    [Fact]
    public async Task Media_Sidecar_Key_Escaping_The_Root_Is_Refused_Without_Disk_Probe()
    {
        using var scope = new TempMediaScope();
        scope.WriteSources();

        DemoCorpusMedia first = new(scope.StorageRoot, scope.MediaDirectory);
        await first.ImagePublicKeyAsync("landing");

        // Hand-tampered sidecar: a traversal key must be refused before any
        // disk probe, aliasing nothing outside the upload root.
        string manifestPath = Path.Combine(scope.MediaDirectory, "produced-keys.json");
        Assert.True(File.Exists(manifestPath));
        string tampered = File.ReadAllText(manifestPath)
            .Replace(
                "\"PublicImageKey\": \"landing/",
                "\"PublicImageKey\": \"../../escape/",
                StringComparison.Ordinal);
        Assert.NotEqual(File.ReadAllText(manifestPath), tampered);
        File.WriteAllText(manifestPath, tampered);

        DemoCorpusMedia replay = new(scope.StorageRoot, scope.MediaDirectory);
        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => replay.ImagePublicKeyAsync("landing"));
        Assert.Contains("resolves outside the upload root", failure.Message, StringComparison.Ordinal);

        // Nothing was written outside the root either.
        Assert.False(File.Exists(Path.Combine(scope.StorageRoot, "..", "escape.jpg")));
    }

    // ----------------------------------------------------------------------
    // CLI group gate (parse-time refusals — the 2026-10-26 incident shape)
    // ----------------------------------------------------------------------

    [Fact]
    public void ProvisionCli_Refuses_An_Empty_Group_List_For_Provision_But_Not_Verify()
    {
        IReadOnlyList<string> forProvision = ProvisionerCliGroups.Refusals([], provisioning: true);
        Assert.Single(forProvision);
        Assert.Contains("must list at least one group", forProvision[0], StringComparison.Ordinal);
        Assert.Contains("portal fixture base entities", forProvision[0], StringComparison.Ordinal);

        // The real recorded verify shape passes no --groups at all; it verifies
        // the target identity and migrations and never writes, so it stays valid.
        Assert.Empty(ProvisionerCliGroups.Refusals([], provisioning: false));
    }

    [Fact]
    public void ProvisionCli_Refuses_A_Raw_Zero_From_A_Shell_Expansion_Accident()
    {
        IReadOnlyList<string> refusals = ProvisionerCliGroups.Refusals(["0"], provisioning: true);
        Assert.Single(refusals);
        Assert.Contains("'0' is not a known group", refusals[0], StringComparison.Ordinal);

        // '0' is neither corpus: it can never expand a write path again.
        Assert.Contains("0", refusals[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ProvisionCli_Refuses_Every_Unknown_Id_But_Keep_The_Real_Command_Shape_Clean()
    {
        // The exact 22-group provision shape the corrected rehearsal run used
        // (and the replay script's --groups list) parses with zero refusals.
        Assert.Empty(ProvisionerCliGroups.Refusals([.. DemoCorpusGroups.All], provisioning: true));

        // The seam-gated fixture groups are known ids too: their seam
        // declaration is judged later, at the provisioner's own gate.
        Assert.Empty(ProvisionerCliGroups.Refusals(
            [.. PortalProvisionGroups.SeamGroups], provisioning: true));

        // Unknown ids are refused in every mode.
        Assert.Single(ProvisionerCliGroups.Refusals(["groups"], provisioning: true));
        Assert.Single(ProvisionerCliGroups.Refusals(["identity-accounts,community"], provisioning: true));
    }


    private static async Task AssertContentAsync(
        DemoCorpusMedia media,
        string key,
        string expectedContentType,
        int expectedLength)
    {
        Result<Sanad.BuildingBlocks.Application.Abstractions.Storage.PrivateFileContent> opened =
            await media.Storage.OpenReadAsync(key);
        Assert.True(opened.IsSuccess, $"{key} should reopen through the storage abstraction");
        Assert.Equal(expectedContentType, opened.Value.ContentType);

        using var buffer = new MemoryStream();
        await opened.Value.Content.CopyToAsync(buffer);
        Assert.Equal(expectedLength, (int)buffer.Length);
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

    /// <summary>A throwaway upload root plus a media directory, deleted on dispose.</summary>
    private sealed class TempMediaScope : IDisposable
    {
        public TempMediaScope()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "sanad-demo-media-tests", Guid.NewGuid().ToString("N"));
            StorageRoot = Path.Combine(root, "storage");
            MediaDirectory = Path.Combine(root, "media");
            Directory.CreateDirectory(StorageRoot);
            Directory.CreateDirectory(MediaDirectory);
        }

        public string StorageRoot { get; }

        public string MediaDirectory { get; }

        public byte[] ImageBytes { get; } = SourceBytes(
            [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9],
            "sanad-demo-jpeg-payload");

        public byte[] DocumentBytes { get; } = Encoding.ASCII.GetBytes(
            "%PDF-1.4\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< /Size 2 >>\n%%EOF\n");

        public byte[] VideoBytes { get; } = SourceBytes(
            [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70], "mp42", "sanad-demo-mp4-payload");

        public string PublicPath(string key) =>
            Path.Combine(StorageRoot, key.Replace('/', Path.DirectorySeparatorChar));

        public void WriteSources()
        {
            File.WriteAllBytes(Path.Combine(MediaDirectory, DemoCorpusMedia.ImageFileName), ImageBytes);
            File.WriteAllBytes(Path.Combine(MediaDirectory, DemoCorpusMedia.DocumentFileName), DocumentBytes);
            File.WriteAllBytes(Path.Combine(MediaDirectory, DemoCorpusMedia.VideoFileName), VideoBytes);
        }

        public void Dispose()
        {
            string root = Directory.GetParent(StorageRoot)!.FullName;
            foreach (string candidate in new[] { StorageRoot, StorageRoot + "-private", root })
            {
                try
                {
                    if (Directory.Exists(candidate))
                    {
                        Directory.Delete(candidate, recursive: true);
                    }
                }
                catch (IOException)
                {
                    // A leaked temp directory is not a test failure.
                }
            }
        }

        private static byte[] SourceBytes(byte[] magic, params string[] tail)
        {
            var buffer = new MemoryStream();
            buffer.Write(magic);
            foreach (string part in tail)
            {
                buffer.Write(Encoding.ASCII.GetBytes(part));
            }

            return buffer.ToArray();
        }
    }
}
