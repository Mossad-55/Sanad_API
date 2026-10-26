using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.CheckIns;
using Sanad.Modules.Families.Domain.HelpRequests;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Domain.Sos;
using Sanad.PortalProvisioner;

namespace Sanad.PortalProvisionerTests;

/// <summary>
/// Focused facts for the OPT-IN <c>elderly-ops</c> demo group: that it is
/// additive to the approved 22-group default (never inside
/// <c>All</c>/<c>ExecutionOrder</c>/<c>DataOnly</c>, never seam-gated, never an
/// S3a fixture group), that the parse-time CLI gate accepts it and the plan
/// LISTS it with its nine-row budget visible when it is explicitly requested,
/// and that the nine-row fixture itself has the approved shape — one open SOS
/// with its single <c>Created</c> history row, one pending help request with its
/// single <c>Created</c> history row, two check-ins on distinct fixed past days,
/// and one <c>Completed</c> finite past medication course whose two dose logs
/// are terminal.
/// <para>
/// Offline throughout: no database, no network, no provider. The domain rows are
/// built through the SAME factories the runtime uses, and the medication's
/// exclusion is asserted through the lateness reader's own selection (its query
/// copied from <c>MedicationLateness.cs</c>, with positive controls) plus the
/// row's own state — never through an invented boolean.
/// </para>
/// </summary>
public sealed class ElderlyOpsFacts
{
    private static readonly DateTime Instant = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The day the approved corpus was executed; every fixture day precedes it.</summary>
    private static readonly DateOnly RunDay = new(2026, 10, 26);

    // ----------------------------------------------------------------------
    // Taxonomy: additive to the approved 22-group default
    // ----------------------------------------------------------------------

    [Fact]
    public void The_OptIn_Group_Leaves_The_Approved_Twenty_Two_Group_Default_Untouched()
    {
        // The approved default is exactly what it was: 22 groups, 19 data-only,
        // 3 seam-gated. The opt-in group expands none of those lists, it adds a
        // fourth classification beside them.
        Assert.Equal(22, DemoCorpusGroups.All.Count);
        Assert.Equal(22, DemoCorpusGroups.ExecutionOrder.Count);
        Assert.Equal(19, DemoCorpusGroups.DataOnly.Count);
        Assert.Equal(3, DemoCorpusGroups.SeamGated.Count);
        Assert.Equal(22, DemoCorpusGroups.ExecutionOrder.Distinct(StringComparer.Ordinal).Count());

        Assert.Equal("elderly-ops", DemoCorpusGroups.ElderlyOps);
        Assert.Equal([DemoCorpusGroups.ElderlyOps], DemoCorpusGroups.OptIn);
        Assert.DoesNotContain(DemoCorpusGroups.ElderlyOps, DemoCorpusGroups.All);
        Assert.DoesNotContain(DemoCorpusGroups.ElderlyOps, DemoCorpusGroups.ExecutionOrder);
        Assert.DoesNotContain(DemoCorpusGroups.ElderlyOps, DemoCorpusGroups.DataOnly);
        Assert.DoesNotContain(DemoCorpusGroups.ElderlyOps, DemoCorpusGroups.SeamGated);

        foreach (string group in DemoCorpusGroups.OptIn)
        {
            Assert.False(DemoCorpusGroups.SeamGated.Contains(group, StringComparer.Ordinal), $"{group} must not be seam-gated");
            Assert.False(DemoCorpusGroups.IsReportFixtureGroup(group), $"{group} must not be an S3a fixture group");
        }

        // It is still a DEMO group, which is what the CLI gate and the
        // provisioner resolve it through.
        Assert.True(DemoCorpusGroups.IsDemoGroup(DemoCorpusGroups.ElderlyOps));
        Assert.False(DemoCorpusGroups.IsDemoGroup("not-a-group"));
        Assert.Equal("demo:demo1:elderly-ops", DemoCorpusIdentity.GroupMarker("demo1", DemoCorpusGroups.ElderlyOps));
    }

    [Fact]
    public void The_OptIn_Group_Declares_The_Approved_Nine_Row_Budget()
    {
        Assert.True(DemoCorpusGroups.ExpectedCounts.ContainsKey(DemoCorpusGroups.ElderlyOps));
        Assert.True(DemoCorpusGroups.Description.ContainsKey(DemoCorpusGroups.ElderlyOps));

        // 2 for the SOS (the row plus its mandatory history row), 2 for the help
        // request, 2 check-ins, 1 medication and 2 terminal dose logs.
        int approvedStoredRows =
            1 /* SOS */
            + ElderlyOpsFixture.SosHistoryRows
            + 1 /* help request */
            + ElderlyOpsFixture.HelpRequestHistoryRows
            + 2 /* check-ins */
            + 1 /* medication */
            + 2 /* dose logs */;

        // The two mandatory history rows are part of the approved shape, so the
        // fixture's budget is nine stored rows in total, not seven — and the
        // plan's own count line states that total.
        Assert.Equal(1, ElderlyOpsFixture.SosHistoryRows);
        Assert.Equal(1, ElderlyOpsFixture.HelpRequestHistoryRows);
        Assert.Equal(9, approvedStoredRows);
        Assert.StartsWith("9 rows:", DemoCorpusGroups.ExpectedCounts[DemoCorpusGroups.ElderlyOps], StringComparison.Ordinal);
        Assert.Equal(
            "9 rows: 1 SOS + 1 SOS history / 1 help request + 1 help-request history / "
                + "2 check-ins / 1 medication / 2 terminal dose logs",
            DemoCorpusGroups.ExpectedCounts[DemoCorpusGroups.ElderlyOps]);
        Assert.False(string.IsNullOrWhiteSpace(DemoCorpusGroups.Description[DemoCorpusGroups.ElderlyOps]));
    }

    [Fact]
    public void The_Parse_Time_Cli_Gate_Accepts_The_OptIn_Group_And_Still_Refuses_Unknown_Ids()
    {
        Assert.Empty(ProvisionerCliGroups.Refusals([DemoCorpusGroups.ElderlyOps], provisioning: true));
        Assert.Empty(ProvisionerCliGroups.Refusals([DemoCorpusGroups.ElderlyOps], provisioning: false));

        // An opt-in group is a real group: naming it satisfies the fail-closed
        // "must list at least one group" rule, while a bare 0 is still refused.
        Assert.Empty(ProvisionerCliGroups.Refusals([], provisioning: false));
        Assert.NotEmpty(ProvisionerCliGroups.Refusals([], provisioning: true));
        Assert.Contains(
            ProvisionerCliGroups.Refusals(["0"], provisioning: true),
            message => message.Contains("not a known group", StringComparison.Ordinal));
        Assert.Contains(
            ProvisionerCliGroups.Refusals([DemoCorpusGroups.ElderlyOps, "0"], provisioning: true),
            message => message.Contains("not a known group", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_Lists_An_Explicitly_Requested_OptIn_Group_With_Its_Nine_Row_Budget()
    {
        DemoCorpusPlanReport plan = DemoCorpusPlan.Build(
            "demo1",
            Instant,
            "rev",
            "rehearsal",
            "C:/tmp/storage",
            [DemoCorpusGroups.ElderlyOps],
            seamDeployed: false,
            localSanadDbOverride: false);

        // Not refused: it is a demo group, it is not seam-gated, and it needs no
        // FixtureRefundSeam allowlist entry.
        Assert.Empty(plan.RefusedGroups);
        Assert.Equal(
            ["no seam-gated group requested: the deployed revision needs no FixtureRefundSeam allowlist entry"],
            plan.SeamAllowlist);

        // The group the provision path WILL write is visible in the write
        // manifest the reviewer approves. It must never be silently dropped from
        // the plan while being written to the target.
        DemoCorpusPlannedGroup planned = Assert.Single(plan.PlannedGroups);
        Assert.Equal(DemoCorpusGroups.ElderlyOps, planned.Group);
        Assert.True(planned.Planned);
        Assert.False(planned.SeamGated);
        Assert.Equal("demo:demo1:elderly-ops", planned.Marker);

        // The approved nine-row budget is visible in the plan itself and is the
        // same nine rows the fixture declares: 1 SOS + 1 SOS history, 1 help
        // request + 1 help-request history, 2 check-ins, 1 medication, 2 dose logs.
        Assert.Equal(
            "9 rows: 1 SOS + 1 SOS history / 1 help request + 1 help-request history / "
                + "2 check-ins / 1 medication / 2 terminal dose logs",
            planned.ExpectedCounts);
        Assert.StartsWith("9 rows:", planned.ExpectedCounts, StringComparison.Ordinal);
        Assert.Contains("2 terminal dose logs", planned.ExpectedCounts, StringComparison.Ordinal);
        Assert.Equal(DemoCorpusGroups.ExpectedCounts[DemoCorpusGroups.ElderlyOps], planned.ExpectedCounts);
        Assert.False(string.IsNullOrWhiteSpace(planned.Description));

        // The approved 22-group taxonomy is untouched by the opt-in: the report's
        // execution order is still exactly the 22-group order, the opt-in group
        // is not in it, and no approved group was added or removed.
        Assert.Equal(22, plan.ExecutionOrder.Count);
        Assert.Equal(DemoCorpusGroups.ExecutionOrder, plan.ExecutionOrder);
        Assert.DoesNotContain(DemoCorpusGroups.ElderlyOps, plan.ExecutionOrder);
        Assert.Equal(DemoCorpusGroups.All.Count, plan.ExecutionOrder.Count);

        // Deterministic and request-order independent: the same request prints an
        // identical planned entry, and so does the same request spelled twice
        // (the group list is a set, not a sequence, for planning purposes).
        Assert.Equal(
            [planned],
            DemoCorpusPlan.Build(
                "demo1", Instant, "rev", "rehearsal", "C:/tmp/storage", [DemoCorpusGroups.ElderlyOps],
                seamDeployed: false, localSanadDbOverride: false).PlannedGroups);
        Assert.Equal(
            [planned],
            DemoCorpusPlan.Build(
                "demo1", Instant, "rev", "rehearsal", "C:/tmp/storage",
                [DemoCorpusGroups.ElderlyOps, DemoCorpusGroups.ElderlyOps],
                seamDeployed: false, localSanadDbOverride: false).PlannedGroups);
    }

    [Fact]
    public void Plan_Orders_A_Mixed_Request_As_The_Approved_Order_Then_The_OptIn_Group()
    {
        // An explicitly requested opt-in group joins the plan AFTER the approved
        // 22-group dependency order, so the print is deterministic and the
        // approved groups keep their approved relative order.
        DemoCorpusPlanReport plan = DemoCorpusPlan.Build(
            "demo1", Instant, "rev", "rehearsal", "C:/tmp/storage",
            [DemoCorpusGroups.ElderlyOps, DemoCorpusGroups.Chat, DemoCorpusGroups.IdentityAccounts],
            seamDeployed: false, localSanadDbOverride: false);

        Assert.Empty(plan.RefusedGroups);
        Assert.Equal(
            [DemoCorpusGroups.IdentityAccounts, DemoCorpusGroups.Chat, DemoCorpusGroups.ElderlyOps],
            plan.PlannedGroups.Select(group => group.Group));

        List<string> approvedOrder = [.. DemoCorpusGroups.ExecutionOrder];
        Assert.True(
            approvedOrder.IndexOf(DemoCorpusGroups.IdentityAccounts) < approvedOrder.IndexOf(DemoCorpusGroups.Chat));

        // The same request in a different order prints the SAME manifest.
        Assert.Equal(
            plan.PlannedGroups,
            DemoCorpusPlan.Build(
                "demo1", Instant, "rev", "rehearsal", "C:/tmp/storage",
                [DemoCorpusGroups.Chat, DemoCorpusGroups.ElderlyOps, DemoCorpusGroups.IdentityAccounts],
                seamDeployed: false, localSanadDbOverride: false).PlannedGroups);
    }

    [Fact]
    public void Plan_Still_Refuses_Unknown_Ids_And_Plans_Nothing_For_An_Empty_Request()
    {
        // An unknown id is still refused by name, and never reaches the plan —
        // even when a valid opt-in group is requested beside it.
        DemoCorpusPlanReport mixed = DemoCorpusPlan.Build(
            "demo1", Instant, "rev", "rehearsal", "C:/tmp/storage",
            [DemoCorpusGroups.ElderlyOps, "0"],
            seamDeployed: false,
            localSanadDbOverride: false);

        Assert.Equal([DemoCorpusGroups.ElderlyOps], mixed.PlannedGroups.Select(group => group.Group));
        Assert.Contains(
            mixed.RefusedGroups,
            message => message.Contains("not a demo-corpus group", StringComparison.Ordinal));

        // An empty request plans nothing and refuses nothing: plan mode is
        // read-only, and the fail-closed "must list at least one group" rule is
        // the parse-time provision gate, not the plan.
        DemoCorpusPlanReport empty = DemoCorpusPlan.Build(
            "demo1", Instant, "rev", "rehearsal", "C:/tmp/storage", [], seamDeployed: false,
            localSanadDbOverride: false);
        Assert.Empty(empty.PlannedGroups);
        Assert.Empty(empty.RefusedGroups);
        Assert.Empty(ProvisionerCliGroups.Refusals([], provisioning: false));
        Assert.NotEmpty(ProvisionerCliGroups.Refusals([], provisioning: true));
    }

    // ----------------------------------------------------------------------
    // Fixture identity: deterministic, unique, within the stored limits
    // ----------------------------------------------------------------------

    [Fact]
    public void The_Fixture_Keys_Are_Deterministic_Unique_And_Within_The_Stored_Limits()
    {
        Assert.Equal("demo:demo1:elderly-ops:sos", ElderlyOpsFixture.SosKey("demo1"));
        Assert.Equal("demo:demo1:elderly-ops:help-request", ElderlyOpsFixture.HelpRequestKey("demo1"));
        Assert.Equal(
            $"SANAD DEMO {ElderlyOpsFixture.MedicationArabicLabel} [demo:demo1:elderly-ops]",
            ElderlyOpsFixture.MedicationName("demo1"));

        // Stable across calls, separated per run tag, and distinct from each
        // other, so no replay can confuse one row's key with another's.
        Assert.Equal(ElderlyOpsFixture.SosKey("demo1"), ElderlyOpsFixture.SosKey("demo1"));
        Assert.NotEqual(ElderlyOpsFixture.SosKey("demo1"), ElderlyOpsFixture.SosKey("demo2"));
        Assert.NotEqual(ElderlyOpsFixture.HelpRequestKey("demo1"), ElderlyOpsFixture.HelpRequestKey("demo2"));
        Assert.NotEqual(ElderlyOpsFixture.SosKey("demo1"), ElderlyOpsFixture.HelpRequestKey("demo1"));
        Assert.NotEqual(ElderlyOpsFixture.MedicationName("demo1"), ElderlyOpsFixture.MedicationName("demo2"));

        // The medications table has no marker column, so the NAME is the
        // ownership key and the instructions carry the same marker.
        string groupMarker = DemoCorpusIdentity.GroupMarker("demo1", DemoCorpusGroups.ElderlyOps);
        Assert.Contains(groupMarker, ElderlyOpsFixture.MedicationName("demo1"), StringComparison.Ordinal);
        Assert.Contains(groupMarker, ElderlyOpsFixture.MedicationInstructions("demo1"), StringComparison.Ordinal);
        Assert.Contains("SANAD DEMO", ElderlyOpsFixture.MedicationName("demo1"), StringComparison.Ordinal);

        // Every stored column limit the fixture writes into.
        Assert.True(ElderlyOpsFixture.SosKey("demo1").Length <= 200);
        Assert.True(ElderlyOpsFixture.HelpRequestKey("demo1").Length <= 200);
        Assert.True(ElderlyOpsFixture.MedicationName("demo1").Length <= Medication.MaximumNameLength);
        Assert.True(ElderlyOpsFixture.MedicationDosage.Length <= Medication.MaximumDosageLength);
        Assert.True(ElderlyOpsFixture.MedicationDoseUnit.Length <= Medication.MaximumDoseUnitLength);
        Assert.True(
            ElderlyOpsFixture.MedicationInstructions("demo1").Length <= Medication.MaximumInstructionsLength);
        foreach (string key in new[]
        {
            ElderlyOpsFixture.HelpRequestActorKey,
            ElderlyOpsFixture.HelpRequestActionKey,
            ElderlyOpsFixture.HelpRequestNeedKey,
            ElderlyOpsFixture.HelpRequestQualifierKey
        })
        {
            Assert.True(key.Length <= 120);
        }

        // No key of this group can collide with a key of the 22-group default:
        // the group marker is part of every one of them.
        foreach (string group in DemoCorpusGroups.All)
        {
            Assert.DoesNotContain(
                DemoCorpusIdentity.GroupMarker("demo1", group),
                ElderlyOpsFixture.SosKey("demo1"),
                StringComparison.Ordinal);
        }
    }

    // ----------------------------------------------------------------------
    // The nine rows, built through the real domain factories
    // ----------------------------------------------------------------------

    [Fact]
    public void The_Sos_Row_Is_Open_Unlocated_And_Carries_Exactly_One_Created_History_Row()
    {
        UserId elderlyIdentity = UserId.New();
        Guid family = Guid.CreateVersion7();
        string key = ElderlyOpsFixture.SosKey("demo1");

        ElderlySos sos = ElderlySos.Create(
            elderlyIdentity, family, key, consent: false, latitude: null, longitude: null);

        Assert.Equal(key, sos.IdempotencyKey);
        Assert.Equal(elderlyIdentity, sos.ElderlyIdentityUserId);
        Assert.Equal(family, sos.FamilyId);
        Assert.Equal(ElderlySosStatus.Open, sos.Status);
        Assert.False(sos.LocationConsentGranted);
        Assert.Null(sos.Latitude);
        Assert.Null(sos.Longitude);

        // The factory adds the mandatory Created history row and nothing else:
        // no acknowledgement, resolution or cancellation is seeded.
        Assert.Equal(ElderlyOpsFixture.SosHistoryRows, sos.History.Count);
        ElderlySosHistory history = Assert.Single(sos.History);
        Assert.Equal(ElderlySosHistoryAction.Created, history.Action);
        Assert.Equal(ElderlySosStatus.Open, history.Status);
        Assert.Equal(elderlyIdentity, history.ActorUserId);

        // Idempotency is KEY-based, never id-based: the same approved input built
        // twice is two different rows, which is exactly why the write path is
        // guarded by (elderly identity, idempotency key) and not by construction.
        Assert.NotEqual(
            sos.Id,
            ElderlySos.Create(elderlyIdentity, family, key, consent: false, latitude: null, longitude: null).Id);
    }

    [Fact]
    public void The_Help_Request_Row_Is_Pending_And_Carries_Exactly_One_Created_History_Row()
    {
        UserId elderlyIdentity = UserId.New();
        Guid family = Guid.CreateVersion7();
        string key = ElderlyOpsFixture.HelpRequestKey("demo1");

        ElderlyHelpRequest request = ElderlyHelpRequest.Create(
            elderlyIdentity,
            family,
            ElderlyOpsFixture.HelpRequestActorKey,
            ElderlyOpsFixture.HelpRequestActorArabicLabel,
            ElderlyOpsFixture.HelpRequestActorEnglishLabel,
            ElderlyOpsFixture.HelpRequestActionKey,
            ElderlyOpsFixture.HelpRequestActionArabicLabel,
            ElderlyOpsFixture.HelpRequestActionEnglishLabel,
            ElderlyOpsFixture.HelpRequestNeedKey,
            ElderlyOpsFixture.HelpRequestNeedArabicLabel,
            ElderlyOpsFixture.HelpRequestNeedEnglishLabel,
            ElderlyOpsFixture.HelpRequestQualifierKey,
            ElderlyOpsFixture.HelpRequestQualifierArabicLabel,
            ElderlyOpsFixture.HelpRequestQualifierEnglishLabel,
            customText: null,
            key);

        Assert.Equal(key, request.IdempotencyKey);
        Assert.Equal(elderlyIdentity, request.ElderlyIdentityUserId);
        Assert.Equal(family, request.FamilyId);
        Assert.Equal(ElderlyHelpRequestStatus.Pending, request.Status);
        Assert.Null(request.CustomText);

        // The stored labels are what the read path projects, so the row is
        // readable without the CMS catalog being seeded.
        Assert.Equal(ElderlyOpsFixture.HelpRequestActorKey, request.ActorKey);
        Assert.Equal(ElderlyOpsFixture.HelpRequestActorArabicLabel, request.ActorArabicLabel);
        Assert.Equal(ElderlyOpsFixture.HelpRequestActorEnglishLabel, request.ActorEnglishLabel);
        Assert.Equal(ElderlyOpsFixture.HelpRequestActionKey, request.ActionKey);
        Assert.Equal(ElderlyOpsFixture.HelpRequestActionArabicLabel, request.ActionArabicLabel);
        Assert.Equal(ElderlyOpsFixture.HelpRequestActionEnglishLabel, request.ActionEnglishLabel);
        Assert.Equal(ElderlyOpsFixture.HelpRequestNeedKey, request.NeedKey);
        Assert.Equal(ElderlyOpsFixture.HelpRequestNeedArabicLabel, request.NeedArabicLabel);
        Assert.Equal(ElderlyOpsFixture.HelpRequestNeedEnglishLabel, request.NeedEnglishLabel);
        Assert.Equal(ElderlyOpsFixture.HelpRequestQualifierKey, request.QualifierKey);
        Assert.Equal(ElderlyOpsFixture.HelpRequestQualifierArabicLabel, request.QualifierArabicLabel);
        Assert.Equal(ElderlyOpsFixture.HelpRequestQualifierEnglishLabel, request.QualifierEnglishLabel);

        // One mandatory Created history row, and no transition beyond it.
        Assert.Equal(ElderlyOpsFixture.HelpRequestHistoryRows, request.History.Count);
        ElderlyHelpRequestHistory history = Assert.Single(request.History);
        Assert.Equal(ElderlyHelpRequestHistoryAction.Created, history.Action);
        Assert.Equal(ElderlyHelpRequestStatus.Pending, history.Status);
        Assert.Null(history.Reason);
    }

    [Fact]
    public void The_Two_Check_Ins_Answer_Two_Distinct_Fixed_Past_Days_In_The_Elderly_Zone()
    {
        Assert.NotEqual(ElderlyOpsFixture.FirstCheckInLocalDate, ElderlyOpsFixture.SecondCheckInLocalDate);
        Assert.NotEqual(ElderlyOpsFixture.FirstCheckInLocalTime, ElderlyOpsFixture.SecondCheckInLocalTime);

        // FIXED days, not offsets: a replay — even one carrying a later run
        // instant — re-reads the same two rows instead of materializing a second
        // pair on shifted days. Both days precede the approved corpus execution.
        Assert.True(ElderlyOpsFixture.FirstCheckInLocalDate < RunDay);
        Assert.True(ElderlyOpsFixture.SecondCheckInLocalDate < RunDay);

        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById(
            ElderlyTimeZone.Normalize(ElderlyTimeZone.InitialDefaultId));
        ElderlyId elderlyId = ElderlyId.New();
        UserId answeredBy = UserId.New();

        (DateOnly Day, TimeOnly LocalTime, bool Answer)[] plan =
        [
            (ElderlyOpsFixture.FirstCheckInLocalDate, ElderlyOpsFixture.FirstCheckInLocalTime,
                ElderlyOpsFixture.FirstCheckInAnswer),
            (ElderlyOpsFixture.SecondCheckInLocalDate, ElderlyOpsFixture.SecondCheckInLocalTime,
                ElderlyOpsFixture.SecondCheckInAnswer)
        ];

        var ids = new List<Guid>();
        foreach ((DateOnly day, TimeOnly localTime, bool answer) in plan)
        {
            // The fixture stamps the answer the way the real submission does:
            // the local wall-clock answer converted to UTC from the elderly
            // profile's own zone, so the instant and the local day agree.
            DateTime answeredOnUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(day, localTime), zone);
            Assert.Equal(DateTimeKind.Utc, answeredOnUtc.Kind);
            Assert.Equal(day, DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(answeredOnUtc, zone)));
            Assert.True(answeredOnUtc < Instant.AddDays(1));

            ElderlyCheckIn checkIn = ElderlyCheckIn.Create(elderlyId, day, answer, localTime, answeredOnUtc, answeredBy);
            Assert.Equal(elderlyId, checkIn.ElderlyId);
            Assert.Equal(day, checkIn.LocalDate);
            Assert.Equal(answer, checkIn.Answer);
            Assert.Equal(localTime, checkIn.AnsweredAtLocalTime);
            Assert.Equal(answeredOnUtc, checkIn.AnsweredOnUtc);
            Assert.Equal(answeredBy, checkIn.AnsweredByUserId);
            ids.Add(checkIn.Id);
        }

        // Two rows, two ids, two distinct (elderly, day) slots — the natural key
        // the unique index enforces.
        Assert.Equal(2, ids.Count);
        Assert.Equal(2, ids.Distinct().Count());
        Assert.True(ElderlyOpsFixture.FirstCheckInAnswer);
        Assert.False(ElderlyOpsFixture.SecondCheckInAnswer);
    }

    [Fact]
    public void The_Medication_Is_A_Completed_Finite_Past_Course_The_Lateness_Reader_Never_Matches()
    {
        // Course shape: finite, ordered, and its two dose days sit inside it.
        Assert.True(ElderlyOpsFixture.MedicationStartDate < ElderlyOpsFixture.MedicationEndDate);
        Assert.True(ElderlyOpsFixture.MedicationEndDate < RunDay);
        Assert.True(ElderlyOpsFixture.MedicationStartDate <= ElderlyOpsFixture.TakenDoseDate);
        Assert.True(ElderlyOpsFixture.TakenDoseDate < ElderlyOpsFixture.SkippedDoseDate);
        Assert.True(ElderlyOpsFixture.SkippedDoseDate <= ElderlyOpsFixture.MedicationEndDate);

        ElderlyId elderlyId = ElderlyId.New();
        UserId owner = UserId.New();

        Medication medication = Medication.Create(
            elderlyId,
            owner,
            ElderlyOpsFixture.MedicationName("demo1"),
            ElderlyOpsFixture.MedicationDosage,
            ElderlyOpsFixture.MedicationDoseUnit,
            ElderlyOpsFixture.MedicationDoseQuantity,
            [ElderlyOpsFixture.MedicationDoseTime],
            ElderlyOpsFixture.MedicationStartDate,
            ElderlyOpsFixture.MedicationEndDate,
            ElderlyOpsFixture.MedicationInstructions("demo1"));

        // The factory's Active state is never persisted: the fixture completes
        // the course before the row is added, so the INSERT is already Completed.
        medication.Complete();
        Assert.Equal(MedicationStatus.Completed, medication.Status);
        Assert.Equal(ElderlyOpsFixture.MedicationEndDate, medication.EndDate);
        Assert.Equal([ElderlyOpsFixture.MedicationDoseTime], medication.DoseTimes);

        // Direct reads of the two selector conjuncts the fixture row fails, so
        // the exclusion does not depend only on the copied query below:
        // Completed is never Active, and the course ended in the past.
        Assert.NotEqual(MedicationStatus.Active, medication.Status);
        Assert.True(medication.EndDate.HasValue && medication.EndDate.Value < RunDay);

        AssertTheLatenessReaderNeverSelects(medication, elderlyId);

        DateTime takenAtUtc = DateTime.SpecifyKind(
            ElderlyOpsFixture.TakenDoseDate.ToDateTime(ElderlyOpsFixture.MedicationDoseTime), DateTimeKind.Utc);

        MedicationDoseLog taken = MedicationDoseLog.CreateScheduled(
            medication.Id, elderlyId, ElderlyOpsFixture.TakenDoseDate, ElderlyOpsFixture.MedicationDoseTime);
        taken.MarkAsTaken(owner, takenAtUtc);
        Assert.Equal(DoseStatus.Taken, taken.Status);
        Assert.Equal(takenAtUtc, taken.TakenAtUtc);
        Assert.Null(taken.SkippedAtUtc);
        Assert.Equal(owner, taken.LoggedByUserId);

        DateTime skippedAtUtc = takenAtUtc.AddDays(1);
        MedicationDoseLog skipped = MedicationDoseLog.CreateScheduled(
            medication.Id, elderlyId, ElderlyOpsFixture.SkippedDoseDate, ElderlyOpsFixture.MedicationDoseTime);
        skipped.MarkAsSkipped(owner, skippedAtUtc, "SANAD DEMO demo-corpus dose: recorded as skipped in the fixture.");
        Assert.Equal(DoseStatus.Skipped, skipped.Status);
        Assert.Equal(skippedAtUtc, skipped.SkippedAtUtc);
        Assert.Null(skipped.TakenAtUtc);
        Assert.Equal(owner, skipped.LoggedByUserId);

        // Two rows, one slot each: the distinct (medication, day, time) triple
        // the unique index allows once per slot.
        Assert.NotEqual(taken.ScheduledDate, skipped.ScheduledDate);
        Assert.Equal(ElderlyOpsFixture.MedicationDoseTime, taken.ScheduledTime);
        Assert.Equal(ElderlyOpsFixture.MedicationDoseTime, skipped.ScheduledTime);
        Assert.NotEqual(taken.Id, skipped.Id);

        // Both rows are TERMINAL: no further transition is possible, and the
        // missed-materialization path is a no-op, so no evaluator can turn the
        // fixture into a late alert.
        Assert.Equal(DoseStatus.Taken, taken.Status);
        Assert.Throws<DomainException>(() => taken.MarkAsSkipped(owner, skippedAtUtc));
        Assert.Throws<DomainException>(() => taken.MarkAsTaken(owner, skippedAtUtc));
        Assert.Throws<DomainException>(() => skipped.MarkAsTaken(owner, skippedAtUtc));
        Assert.Throws<DomainException>(() => skipped.MarkAsSkipped(owner, skippedAtUtc));
        skipped.MarkAsMissed(skippedAtUtc.AddDays(1));
        Assert.Equal(DoseStatus.Skipped, skipped.Status);
        taken.MarkAsMissed(skippedAtUtc.AddDays(1));
        Assert.Equal(DoseStatus.Taken, taken.Status);
    }

    // ----------------------------------------------------------------------
    // Shared invariants
    // ----------------------------------------------------------------------

    /// <summary>
    /// The lateness reader's OWN selection, copied from
    /// <c>MedicationLateness.cs</c> (<c>MedicationLatenessEvaluation.RunAsync</c>):
    /// <c>db.Medications.Where(x =&gt; x.ElderlyId == elderly.Id &amp;&amp; x.Status == MedicationStatus.Active
    /// &amp;&amp; x.StartDate &lt;= localDate &amp;&amp; (!x.EndDate.HasValue || x.EndDate &gt;= localDate))</c>.
    /// Evaluated here as a QUERY over the real fixture rows rather than as an
    /// invented boolean, with a positive control at every call site so a
    /// predicate that selected nothing can never pass.
    /// <para>
    /// The selector itself is <c>internal</c> and its entry point needs an
    /// <c>IFamiliesDbContext</c> (forty <c>DbSet</c> members, satisfiable only
    /// with an EF provider this project does not reference), so it cannot be
    /// invoked from here; it IS exercised through its public handler on EF
    /// InMemory by <c>tests/Sanad.UnitTests/Families/MedicationLatenessEvaluationTests.cs</c>.
    /// </para>
    /// </summary>
    private static Guid[] SelectAsTheLatenessReader(
        IEnumerable<Medication> medications, ElderlyId elderlyId, DateOnly localDate) =>
        medications
            .AsQueryable()
            .Where(x => x.ElderlyId == elderlyId
                && x.Status == MedicationStatus.Active
                && x.StartDate <= localDate
                && (!x.EndDate.HasValue || x.EndDate >= localDate))
            .Select(x => x.Id.Value)
            .ToArray();

    /// <summary>
    /// The fixture medication is excluded on BOTH axes, so the exclusion cannot
    /// expire: <c>Completed</c> excludes it on any day, and the finite past
    /// <c>EndDate</c> closes the window on every day after the course. Asserted
    /// for an in-course day, the course end, the corpus day and a far-future day,
    /// against a POSITIVE CONTROL built from the same factory: an <c>Active</c>
    /// medication over the identical window, which the same query DOES select on
    /// the in-course day (so the "nothing selected" result is the fixture row's
    /// own state) and does NOT select the day before its window or after its end
    /// (so the date conjuncts are live too).
    /// </summary>
    private static void AssertTheLatenessReaderNeverSelects(Medication medication, ElderlyId elderlyId)
    {
        Medication activeControl = Medication.Create(
            elderlyId,
            UserId.New(),
            "SANAD DEMO control: active course over the identical window",
            ElderlyOpsFixture.MedicationDosage,
            ElderlyOpsFixture.MedicationDoseUnit,
            ElderlyOpsFixture.MedicationDoseQuantity,
            [ElderlyOpsFixture.MedicationDoseTime],
            ElderlyOpsFixture.MedicationStartDate,
            endDate: ElderlyOpsFixture.MedicationEndDate);

        // The control is Active and in-window: the query MUST select it on the
        // in-course day. On that day the fixture medication is inside the same
        // window and differs only by Status, which isolates the Active conjunct.
        Assert.Equal(MedicationStatus.Active, activeControl.Status);
        Assert.Equal(
            [activeControl.Id.Value],
            SelectAsTheLatenessReader([medication, activeControl], elderlyId, ElderlyOpsFixture.MedicationStartDate));

        // Outside the control's window nothing is selected, so the date
        // conjuncts are live rather than vacuously ignored.
        Assert.Empty(
            SelectAsTheLatenessReader([medication, activeControl], elderlyId, ElderlyOpsFixture.MedicationStartDate.AddDays(-1)));
        Assert.Empty(SelectAsTheLatenessReader([medication, activeControl], elderlyId, ElderlyOpsFixture.MedicationEndDate.AddDays(1)));

        // The fixture row alone is never selected: not on an in-course day, not
        // on the course end, not on the corpus day and not far in the future.
        Assert.Empty(SelectAsTheLatenessReader([medication], elderlyId, ElderlyOpsFixture.MedicationStartDate));
        Assert.Empty(SelectAsTheLatenessReader([medication], elderlyId, ElderlyOpsFixture.MedicationEndDate));
        Assert.Empty(SelectAsTheLatenessReader([medication], elderlyId, RunDay));
        Assert.Empty(SelectAsTheLatenessReader([medication], elderlyId, RunDay.AddYears(5)));
    }
}
