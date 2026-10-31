using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.CheckIns;
using Ahlena.Modules.Families.Application.HelpRequests;
using Ahlena.Modules.Families.Application.Sos;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Elderlies.CheckIns;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Domain.HelpRequests;
using Ahlena.Modules.Families.Domain.Medications;
using Ahlena.Modules.Families.Domain.Sos;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// F1 relational proof for the Admin elderly-ID operational filters against a
/// validated disposable PostgreSQL target. The exact production read query
/// handlers (check-in list/detail/timeline/aggregate, Admin SOS list/detail/
/// history, Admin help-request list/detail/history/aggregate) execute through
/// Npgsql so a non-translatable predicate fails loudly instead of silently
/// passing on the InMemory provider.
/// <para>
/// The target uses credentials from the local PostgreSQL named by
/// <c>ConnectionStrings__IdentityIntegrationDatabase</c>, but replaces the
/// database with a unique nonce-bearing name. The source must be loopback
/// PostgreSQL on port 5432, and the generated target is verified absent before
/// creation. This class never connects to the source database itself or to
/// persistent <c>AhlenaDb</c>. It applies the committed Families migrations to
/// the new target, cleans inserted rows, and deletes only the unique database it
/// created. When the base connection string is absent the tests skip.
/// </para>
/// </summary>
[Collection("F1ElderlyFilterVerification")]
public sealed class AdminElderlyFilterPostgresTests : IAsyncLifetime
{
    private const string ConnectionStringVariable =
        "ConnectionStrings__IdentityIntegrationDatabase";

    // A per-test-process unique target prevents the test from deleting or
    // reusing a prior database, even if a previous run was interrupted.
    private static readonly string ExpectedDatabase =
        "AhlenaF1Verify_" + Guid.NewGuid().ToString("N");

    private static readonly DateOnly FirstDate = new(2031, 2, 1);

    private static readonly DateOnly SecondDate = new(2031, 2, 2);

    private static readonly SemaphoreSlim MigrationLock = new(1, 1);

    private static string? _migratedFor;
    private static bool _createdTarget;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!_createdTarget || string.IsNullOrWhiteSpace(_migratedFor))
        {
            return;
        }

        // This exact nonce-bearing database was verified absent before this
        // process created it. Cleanup cannot target AhlenaDb, Bruno, or any
        // pre-existing fixed-name database.
        await using FamiliesDbContext db = CreateDb(_migratedFor);
        await db.Database.EnsureDeletedAsync();
        _createdTarget = false;
        _migratedFor = null;
    }

    [F1PostgresFact]
    public async Task OriginalTypedIdValuePredicates_AreNotTranslatedByPostgres()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        string token = NewToken();
        Seed seed = await SeedAsync(connectionString, token, CancellationToken.None);
        try
        {
            await using var db = CreateDb(connectionString);

            // These mirror the pre-F1 expressions exactly. Keep the original
            // predicates executable beside the handler regressions so the
            // test records whether PostgreSQL actually rejects `.Value`,
            // rather than treating the suspected cause as proven by assertion.
            Guid? filterId = seed.FirstElderlyId;
            IQueryable<int> checkIns = db.ElderlyCheckIns.AsNoTracking()
                .Join(db.Elderlies.AsNoTracking(), checkIn => checkIn.ElderlyId,
                    elderly => elderly.Id, (checkIn, elderly) => new { checkIn, elderly })
                .Where(x => x.elderly.Id.Value == seed.FirstElderlyId)
                .Select(_ => 1);

            IQueryable<int> sos = db.ElderlySos.AsNoTracking()
                .Join(db.Elderlies.AsNoTracking(), item => item.ElderlyIdentityUserId,
                    elderly => elderly.IdentityUserId, (item, elderly) => new { item, elderly })
                .Where(x => x.elderly.Id.Value == filterId)
                .Select(_ => 1);

            IQueryable<int> helpRequests = db.ElderlyHelpRequests.AsNoTracking()
                .Join(db.Elderlies.AsNoTracking(), item => item.ElderlyIdentityUserId,
                    elderly => elderly.IdentityUserId, (item, elderly) => new { item, elderly })
                .Where(x => x.elderly.Id.Value == filterId)
                .Select(_ => 1);

            await AssertCannotTranslateAsync(checkIns);
            await AssertCannotTranslateAsync(sos);
            await AssertCannotTranslateAsync(helpRequests);
        }
        finally
        {
            await CleanupAsync(connectionString, seed, token);
        }
    }

    [F1PostgresFact]
    public async Task CheckInList_ExistingUnknownAndUnfilteredElderlyId_AreScopedAndAudited()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        string token = NewToken();
        Seed seed = await SeedAsync(connectionString, token, CancellationToken.None);
        try
        {
            await using var db = CreateDb(connectionString);
            UserId actor = UserId.New();

            var byExisting = await new GetAdminElderlyCheckInsQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-list-existing", ElderlyId: seed.FirstElderlyId),
                CancellationToken.None);
            Assert.True(byExisting.IsSuccess);
            Assert.Equal(2, byExisting.Value.TotalCount);
            Assert.Equal(
                new[] { seed.FirstCheckInId, seed.FirstSecondCheckInId }.OrderBy(x => x).ToArray(),
                byExisting.Value.Items.Select(x => x.Id).OrderBy(x => x).ToArray());
            Assert.All(byExisting.Value.Items, x => Assert.Equal(seed.FirstElderlyId, x.ElderlyId));

            var byUnknown = await new GetAdminElderlyCheckInsQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-list-unknown", ElderlyId: Guid.NewGuid()),
                CancellationToken.None);
            Assert.True(byUnknown.IsSuccess);
            Assert.Equal(0, byUnknown.Value.TotalCount);
            Assert.Empty(byUnknown.Value.Items);

            var unfiltered = await new GetAdminElderlyCheckInsQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-list-all", PageSize: 100),
                CancellationToken.None);
            Assert.True(unfiltered.IsSuccess);
            Assert.Equal(3, unfiltered.Value.TotalCount);
            Assert.Contains(seed.FirstCheckInId, unfiltered.Value.Items.Select(x => x.Id));
            Assert.Contains(seed.FirstSecondCheckInId, unfiltered.Value.Items.Select(x => x.Id));
            Assert.Contains(seed.SecondElderlyCheckInId, unfiltered.Value.Items.Select(x => x.Id));
            Assert.DoesNotContain(seed.DeletedCheckInId, unfiltered.Value.Items.Select(x => x.Id));

            var byAnswer = await new GetAdminElderlyCheckInsQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-list-negative", Answer: false),
                CancellationToken.None);
            Assert.True(byAnswer.IsSuccess);
            Assert.Equal(seed.FirstSecondCheckInId, Assert.Single(byAnswer.Value.Items).Id);

            var audits = await db.AdminMedicationAccessAudits.AsNoTracking()
                .Where(x => x.CorrelationId.StartsWith(token)).ToListAsync();
            Assert.Equal(4, audits.Count);
            Assert.All(audits, x =>
            {
                Assert.Equal("ListCheckIns", x.Action);
                Assert.Equal("CheckIn", x.ResourceType);
                Assert.Null(x.ResourceId);
                Assert.Equal("SupportAdmin", x.ActorAccountType);
                Assert.Equal(actor, x.ActorUserId);
            });
        }
        finally
        {
            await CleanupAsync(connectionString, seed, token);
        }
    }

    [F1PostgresFact]
    public async Task CheckInAggregate_ElderlyIdAndDateFilters_MatchFilters()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        string token = NewToken();
        Seed seed = await SeedAsync(connectionString, token, CancellationToken.None);
        try
        {
            await using var db = CreateDb(connectionString);
            UserId actor = UserId.New();

            var byElderly = await new GetAdminElderlyCheckInAggregateQueryHandler(db).Handle(
                new(actor, "SuperAdmin", token + "-aggregate-elderly", seed.FirstElderlyId),
                CancellationToken.None);
            Assert.True(byElderly.IsSuccess);
            Assert.Equal(2, byElderly.Value.Total);
            Assert.Equal(1, byElderly.Value.Positive);
            Assert.Equal(1, byElderly.Value.Negative);

            var byUnknown = await new GetAdminElderlyCheckInAggregateQueryHandler(db).Handle(
                new(actor, "SuperAdmin", token + "-aggregate-unknown", Guid.NewGuid()),
                CancellationToken.None);
            Assert.True(byUnknown.IsSuccess);
            Assert.Equal(0, byUnknown.Value.Total);
            Assert.Equal(0, byUnknown.Value.Positive);
            Assert.Equal(0, byUnknown.Value.Negative);

            var byRange = await new GetAdminElderlyCheckInAggregateQueryHandler(db).Handle(
                new(actor, "SuperAdmin", token + "-aggregate-range", null, FirstDate, SecondDate),
                CancellationToken.None);
            Assert.True(byRange.IsSuccess);
            Assert.Equal(3, byRange.Value.Total);
            Assert.Equal(2, byRange.Value.Positive);
            Assert.Equal(1, byRange.Value.Negative);

            var audits = await db.AdminMedicationAccessAudits.AsNoTracking()
                .Where(x => x.CorrelationId.StartsWith(token)).ToListAsync();
            Assert.Equal(3, audits.Count);
            Assert.All(audits, x => Assert.Equal("GetCheckInAggregate", x.Action));
            Assert.Contains(audits, x => x.ResourceId == seed.FirstElderlyId);
        }
        finally
        {
            await CleanupAsync(connectionString, seed, token);
        }
    }

    [F1PostgresFact]
    public async Task CheckInTimeline_ElderlyIdFilter_IsOrderedAndExcludesDeletedFamily()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        string token = NewToken();
        Seed seed = await SeedAsync(connectionString, token, CancellationToken.None);
        try
        {
            await using var db = CreateDb(connectionString);
            UserId actor = UserId.New();

            var byElderly = await new GetAdminElderlyCheckInTimelineQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-timeline", seed.FirstElderlyId, FirstDate, SecondDate),
                CancellationToken.None);
            Assert.True(byElderly.IsSuccess);
            Assert.Equal([FirstDate, SecondDate], byElderly.Value.Select(x => x.LocalDate));
            Assert.Equal(
                [seed.FirstCheckInId, seed.FirstSecondCheckInId],
                byElderly.Value.Select(x => x.Id));

            var byUnknown = await new GetAdminElderlyCheckInTimelineQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-timeline-unknown", Guid.NewGuid(), FirstDate, SecondDate),
                CancellationToken.None);
            Assert.True(byUnknown.IsSuccess);
            Assert.Empty(byUnknown.Value);

            var byDeletedFamily = await new GetAdminElderlyCheckInTimelineQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-timeline-deleted", seed.DeletedElderlyId, FirstDate, SecondDate),
                CancellationToken.None);
            Assert.True(byDeletedFamily.IsSuccess);
            Assert.Empty(byDeletedFamily.Value);

            var audits = await db.AdminMedicationAccessAudits.AsNoTracking()
                .Where(x => x.CorrelationId.StartsWith(token)).ToListAsync();
            Assert.Equal(3, audits.Count);
            Assert.All(audits, x => Assert.Equal("GetCheckInTimeline", x.Action));
            Assert.Contains(audits, x => x.ResourceId == seed.FirstElderlyId);
            Assert.Contains(audits, x => x.ResourceId == seed.DeletedElderlyId);
        }
        finally
        {
            await CleanupAsync(connectionString, seed, token);
        }
    }

    [F1PostgresFact]
    public async Task SosList_ElderlyIdFilter_IsScopedAndExcludesDeletedFamily()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        string token = NewToken();
        Seed seed = await SeedAsync(connectionString, token, CancellationToken.None);
        try
        {
            await using var db = CreateDb(connectionString);
            UserId actor = UserId.New();

            var byElderly = await new AdminListElderlySosQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-sos-list", null, seed.FirstElderlyId),
                CancellationToken.None);
            Assert.True(byElderly.IsSuccess);
            Assert.Equal(1, byElderly.Value.TotalCount);
            var item = Assert.Single(byElderly.Value.Items);
            Assert.Equal(seed.FirstSosId, item.Id);
            Assert.Equal(seed.FirstElderlyId, item.ElderlyId);
            Assert.Equal(ElderlySosStatus.Open, item.Status);

            var byUnknown = await new AdminListElderlySosQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-sos-list-unknown", null, Guid.NewGuid()),
                CancellationToken.None);
            Assert.True(byUnknown.IsSuccess);
            Assert.Equal(0, byUnknown.Value.TotalCount);
            Assert.Empty(byUnknown.Value.Items);

            var unfiltered = await new AdminListElderlySosQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-sos-list-all", null, null, 1, 100),
                CancellationToken.None);
            Assert.True(unfiltered.IsSuccess);
            Assert.Equal(2, unfiltered.Value.TotalCount);
            Assert.Contains(seed.FirstSosId, unfiltered.Value.Items.Select(x => x.Id));
            Assert.Contains(seed.SecondElderlySosId, unfiltered.Value.Items.Select(x => x.Id));
            Assert.DoesNotContain(seed.DeletedSosId, unfiltered.Value.Items.Select(x => x.Id));

            var audits = await db.AdminMedicationAccessAudits.AsNoTracking()
                .Where(x => x.CorrelationId.StartsWith(token)).ToListAsync();
            Assert.Equal(3, audits.Count);
            Assert.All(audits, x =>
            {
                Assert.Equal("ListSos", x.Action);
                Assert.Equal("Sos", x.ResourceType);
                Assert.Null(x.ResourceId);
            });
        }
        finally
        {
            await CleanupAsync(connectionString, seed, token);
        }
    }

    [F1PostgresFact]
    public async Task HelpRequestList_ElderlyIdFilter_IsScopedAndExcludesDeletedFamily()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        string token = NewToken();
        Seed seed = await SeedAsync(connectionString, token, CancellationToken.None);
        try
        {
            await using var db = CreateDb(connectionString);
            UserId actor = UserId.New();

            var byElderly = await new AdminListElderlyHelpRequestsQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-help-list", null, seed.FirstElderlyId),
                CancellationToken.None);
            Assert.True(byElderly.IsSuccess);
            Assert.Equal(1, byElderly.Value.TotalCount);
            var item = Assert.Single(byElderly.Value.Items);
            Assert.Equal(seed.FirstHelpRequestId, item.Id);
            Assert.Equal(seed.FirstElderlyId, item.ElderlyId);
            Assert.Equal(ElderlyHelpRequestStatus.Pending, item.Status);

            var byUnknown = await new AdminListElderlyHelpRequestsQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-help-list-unknown", null, Guid.NewGuid()),
                CancellationToken.None);
            Assert.True(byUnknown.IsSuccess);
            Assert.Equal(0, byUnknown.Value.TotalCount);
            Assert.Empty(byUnknown.Value.Items);

            var unfiltered = await new AdminListElderlyHelpRequestsQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-help-list-all", null, null, 1, 100),
                CancellationToken.None);
            Assert.True(unfiltered.IsSuccess);
            Assert.Equal(2, unfiltered.Value.TotalCount);
            Assert.Contains(seed.FirstHelpRequestId, unfiltered.Value.Items.Select(x => x.Id));
            Assert.Contains(seed.SecondElderlyHelpRequestId, unfiltered.Value.Items.Select(x => x.Id));
            Assert.DoesNotContain(seed.DeletedHelpRequestId, unfiltered.Value.Items.Select(x => x.Id));

            var audits = await db.AdminMedicationAccessAudits.AsNoTracking()
                .Where(x => x.CorrelationId.StartsWith(token)).ToListAsync();
            Assert.Equal(3, audits.Count);
            Assert.All(audits, x =>
            {
                Assert.Equal("ListHelpRequests", x.Action);
                Assert.Equal("HelpRequest", x.ResourceType);
                Assert.Null(x.ResourceId);
            });
        }
        finally
        {
            await CleanupAsync(connectionString, seed, token);
        }
    }

    [F1PostgresFact]
    public async Task HelpRequestAggregate_ExcludesDeletedFamilyRows()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        string token = NewToken();
        Seed seed = await SeedAsync(connectionString, token, CancellationToken.None);
        try
        {
            await using var db = CreateDb(connectionString);
            UserId actor = UserId.New();

            var result = await new AdminGetElderlyHelpRequestAggregateQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-help-aggregate"),
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.True(result.Value.TryGetValue(ElderlyHelpRequestStatus.Pending, out int pending));
            Assert.Equal(2, pending);
            Assert.Equal(2, result.Value.Values.Sum());

            var audit = Assert.Single(await db.AdminMedicationAccessAudits.AsNoTracking()
                .Where(x => x.CorrelationId.StartsWith(token)).ToListAsync());
            Assert.Equal("GetHelpRequestAggregate", audit.Action);
            Assert.Equal("HelpRequest", audit.ResourceType);
            Assert.Null(audit.ResourceId);
        }
        finally
        {
            await CleanupAsync(connectionString, seed, token);
        }
    }

    [F1PostgresFact]
    public async Task AdminAccessAudits_RecordExpectedPayloadFreeRowsForEveryAccess()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        string token = NewToken();
        Seed seed = await SeedAsync(connectionString, token, CancellationToken.None);
        try
        {
            await using var db = CreateDb(connectionString);
            UserId actor = UserId.New();

            Assert.True((await new GetAdminElderlyCheckInsQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-1", ElderlyId: seed.FirstElderlyId),
                CancellationToken.None)).IsSuccess);
            Assert.True((await new GetAdminElderlyCheckInQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-2", seed.FirstCheckInId),
                CancellationToken.None)).IsSuccess);
            Assert.True((await new GetAdminElderlyCheckInTimelineQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-3", seed.FirstElderlyId, FirstDate, SecondDate),
                CancellationToken.None)).IsSuccess);
            Assert.True((await new GetAdminElderlyCheckInAggregateQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-4", seed.FirstElderlyId),
                CancellationToken.None)).IsSuccess);
            Assert.True((await new AdminListElderlySosQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-5"),
                CancellationToken.None)).IsSuccess);
            Assert.True((await new AdminGetElderlySosQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-6", seed.FirstSosId),
                CancellationToken.None)).IsSuccess);
            Assert.True((await new AdminGetElderlySosHistoryQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-7", seed.FirstSosId),
                CancellationToken.None)).IsSuccess);
            Assert.True((await new AdminListElderlyHelpRequestsQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-8"),
                CancellationToken.None)).IsSuccess);
            Assert.True((await new AdminGetElderlyHelpRequestQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-9", seed.FirstHelpRequestId),
                CancellationToken.None)).IsSuccess);
            Assert.True((await new AdminGetElderlyHelpRequestHistoryQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-10", seed.FirstHelpRequestId),
                CancellationToken.None)).IsSuccess);
            Assert.True((await new AdminGetElderlyHelpRequestAggregateQueryHandler(db).Handle(
                new(actor, "SupportAdmin", token + "-audit-11"),
                CancellationToken.None)).IsSuccess);

            List<AdminMedicationAccessAudit> audits =
                await db.AdminMedicationAccessAudits.AsNoTracking()
                    .Where(x => x.CorrelationId.StartsWith(token)).ToListAsync();

            string[] expected = [
                "ListCheckIns|CheckIn|",
                "GetCheckIn|CheckIn|" + seed.FirstCheckInId,
                "GetCheckInTimeline|CheckIn|" + seed.FirstElderlyId,
                "GetCheckInAggregate|CheckIn|" + seed.FirstElderlyId,
                "ListSos|Sos|",
                "GetSos|Sos|" + seed.FirstSosId,
                "GetSosHistory|Sos|" + seed.FirstSosId,
                "ListHelpRequests|HelpRequest|",
                "GetHelpRequest|HelpRequest|" + seed.FirstHelpRequestId,
                "GetHelpRequestHistory|HelpRequest|" + seed.FirstHelpRequestId,
                "GetHelpRequestAggregate|HelpRequest|"
            ];

            Assert.Equal(
                expected.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                audits.Select(x => $"{x.Action}|{x.ResourceType}|{x.ResourceId}")
                    .OrderBy(x => x, StringComparer.Ordinal).ToArray());
            Assert.All(audits, x =>
            {
                Assert.False(string.IsNullOrWhiteSpace(x.CorrelationId));
                Assert.Equal("SupportAdmin", x.ActorAccountType);
                Assert.Equal(actor, x.ActorUserId);
            });
            Assert.All(
                typeof(AdminMedicationAccessAudit).GetProperties(),
                property =>
                {
                    Assert.DoesNotContain("Name", property.Name);
                    Assert.DoesNotContain("Medical", property.Name);
                    Assert.DoesNotContain("Payload", property.Name);
                });
        }
        finally
        {
            await CleanupAsync(connectionString, seed, token);
        }
    }

    private static string NewToken() => "f1-" + Guid.NewGuid().ToString("N");

    private static async Task<Seed> SeedAsync(
        string connectionString,
        string token,
        CancellationToken ct)
    {
        await using var db = CreateDb(connectionString);

        UserId liveOwner = UserId.New();
        UserId otherOwner = UserId.New();
        UserId deletedOwner = UserId.New();
        UserId actor = UserId.New();
        DateOnly today = new(2031, 1, 1);

        Family liveFamily = Family.Create(liveOwner, $"F1 live {token}");
        Family otherFamily = Family.Create(otherOwner, $"F1 other {token}");
        Family deletedFamily = Family.Create(deletedOwner, $"F1 deleted {token}");
        deletedFamily.MarkDeleted("f1 verification", null);

        Elderly first = Elderly.Create(
            liveOwner, UserId.New(), liveFamily.Id, FamilyRelationshipType.Father,
            FullName.Create("الابن"), FullName.Create($"F1 first {token}"),
            Gender.Male, new DateOnly(1950, 1, 1), today);
        Elderly second = Elderly.Create(
            otherOwner, UserId.New(), otherFamily.Id, FamilyRelationshipType.Mother,
            FullName.Create("الأم"), FullName.Create($"F1 second {token}"),
            Gender.Female, new DateOnly(1952, 1, 1), today);
        Elderly gone = Elderly.Create(
            deletedOwner, UserId.New(), deletedFamily.Id, FamilyRelationshipType.Father,
            FullName.Create("المحذوف"), FullName.Create($"F1 deleted {token}"),
            Gender.Male, new DateOnly(1951, 1, 1), today);

        ElderlyCheckIn firstCheckIn = ElderlyCheckIn.Create(
            first.Id, FirstDate, true, new TimeOnly(8, 0),
            new DateTime(2031, 2, 1, 6, 0, 0, DateTimeKind.Utc), actor);
        ElderlyCheckIn firstNegativeCheckIn = ElderlyCheckIn.Create(
            first.Id, SecondDate, false, new TimeOnly(8, 0),
            new DateTime(2031, 2, 2, 6, 0, 0, DateTimeKind.Utc), actor);
        ElderlyCheckIn secondCheckIn = ElderlyCheckIn.Create(
            second.Id, FirstDate, true, new TimeOnly(9, 0),
            new DateTime(2031, 2, 1, 7, 0, 0, DateTimeKind.Utc), actor);
        ElderlyCheckIn deletedCheckIn = ElderlyCheckIn.Create(
            gone.Id, FirstDate, false, new TimeOnly(10, 0),
            new DateTime(2031, 2, 1, 8, 0, 0, DateTimeKind.Utc), actor);

        ElderlySos firstSos = ElderlySos.Create(first.IdentityUserId, liveFamily.Id.Value, token + "-sos-1", true, 30.0m, 31.0m);
        ElderlySos secondSos = ElderlySos.Create(second.IdentityUserId, otherFamily.Id.Value, token + "-sos-2", false, null, null);
        ElderlySos deletedSos = ElderlySos.Create(gone.IdentityUserId, deletedFamily.Id.Value, token + "-sos-3", false, null, null);

        ElderlyHelpRequest firstRequest = NewHelpRequest(first.IdentityUserId, liveFamily.Id.Value, token + "-help-1");
        ElderlyHelpRequest secondRequest = NewHelpRequest(second.IdentityUserId, otherFamily.Id.Value, token + "-help-2");
        ElderlyHelpRequest deletedRequest = NewHelpRequest(gone.IdentityUserId, deletedFamily.Id.Value, token + "-help-3");

        db.AddRange(liveFamily, otherFamily, deletedFamily, first, second, gone);
        db.AddRange(firstCheckIn, firstNegativeCheckIn, secondCheckIn, deletedCheckIn);
        db.AddRange(firstSos, secondSos, deletedSos);
        db.AddRange(firstRequest, secondRequest, deletedRequest);
        await db.SaveChangesAsync(ct);

        return new Seed(
            FamilyIds: [liveFamily.Id.Value, otherFamily.Id.Value, deletedFamily.Id.Value],
            ElderlyIds: [first.Id.Value, second.Id.Value, gone.Id.Value],
            CheckInIds: [firstCheckIn.Id, firstNegativeCheckIn.Id, secondCheckIn.Id, deletedCheckIn.Id],
            SosIds: [firstSos.Id, secondSos.Id, deletedSos.Id],
            HelpRequestIds: [firstRequest.Id, secondRequest.Id, deletedRequest.Id],
            FirstElderlyId: first.Id.Value,
            SecondElderlyId: second.Id.Value,
            DeletedElderlyId: gone.Id.Value,
            FirstCheckInId: firstCheckIn.Id,
            FirstSecondCheckInId: firstNegativeCheckIn.Id,
            SecondElderlyCheckInId: secondCheckIn.Id,
            DeletedCheckInId: deletedCheckIn.Id,
            FirstSosId: firstSos.Id,
            SecondElderlySosId: secondSos.Id,
            DeletedSosId: deletedSos.Id,
            FirstHelpRequestId: firstRequest.Id,
            SecondElderlyHelpRequestId: secondRequest.Id,
            DeletedHelpRequestId: deletedRequest.Id);
    }

    private static ElderlyHelpRequest NewHelpRequest(UserId elderly, Guid family, string idempotencyKey) =>
        ElderlyHelpRequest.Create(
            elderly, family,
            "actor.self", "الذات", "Self",
            "action.medicine", "دواء", "Medicine",
            "need.medicine", "دواء", "Medicine",
            null, null, null, null, idempotencyKey);

    private static async Task AssertCannotTranslateAsync<T>(IQueryable<T> query)
    {
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => query.CountAsync());
        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CleanupAsync(string connectionString, Seed seed, string token)
    {
        await using var db = CreateDb(connectionString);
        await DeleteByIdsAsync(db, "DELETE FROM families.elderly_check_ins WHERE id = ANY({0})", seed.CheckInIds);
        await DeleteByIdsAsync(db, "DELETE FROM families.elderly_sos WHERE \"Id\" = ANY({0})", seed.SosIds);
        await DeleteByIdsAsync(db, "DELETE FROM families.elderly_help_requests WHERE \"Id\" = ANY({0})", seed.HelpRequestIds);
        await DeleteByIdsAsync(db, "DELETE FROM families.elderlies WHERE id = ANY({0})", seed.ElderlyIds);
        await DeleteByIdsAsync(db, "DELETE FROM families.families WHERE id = ANY({0})", seed.FamilyIds);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM families.admin_medication_access_audits WHERE correlation_id LIKE {0}",
            new object[] { token + "%" });
    }

    private static async Task DeleteByIdsAsync(
        FamiliesDbContext db,
        string sql,
        IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        await db.Database.ExecuteSqlRawAsync(sql, new object[] { ids.ToArray() });
    }

    private static async Task EnsureMigratedAsync(string connectionString)
    {
        if (_migratedFor == connectionString)
        {
            return;
        }

        await MigrationLock.WaitAsync();
        try
        {
            if (_migratedFor == connectionString)
            {
                return;
            }

            await EnsureFreshDatabaseAbsentAsync(connectionString);
            await using var families = CreateDb(connectionString);
            _createdTarget = true;
            await families.Database.MigrateAsync();
            _migratedFor = connectionString;
        }
        finally
        {
            MigrationLock.Release();
        }
    }

    private static string GetValidatedConnectionString()
    {
        string? raw = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException($"Test requires {ConnectionStringVariable}.");
        }

        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1") || builder.Port != 5432)
        {
            throw new InvalidOperationException(
                "Refusing to write: the F1 verification source must be localhost:5432 PostgreSQL.");
        }

        builder.Database = ExpectedDatabase;
        if (!string.Equals(builder.Database, ExpectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Refusing to write: the F1 verification target must be database '{ExpectedDatabase}'.");
        }

        return builder.ConnectionString;
    }

    private static async Task EnsureFreshDatabaseAbsentAsync(string connectionString)
    {
        NpgsqlConnectionStringBuilder maintenance = new(connectionString)
        {
            Database = "postgres"
        };

        await using NpgsqlConnection connection = new(maintenance.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @database)";
        command.Parameters.AddWithValue("database", ExpectedDatabase);
        bool exists = (bool)(await command.ExecuteScalarAsync() ?? false);

        if (exists)
        {
            throw new InvalidOperationException(
                "Refusing to write: the randomly named F1 disposable target already exists; no database was changed.");
        }
    }

    private static FamiliesDbContext CreateDb(string connectionString) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseNpgsql(connectionString).Options);

    private sealed record Seed(
        Guid[] FamilyIds,
        Guid[] ElderlyIds,
        Guid[] CheckInIds,
        Guid[] SosIds,
        Guid[] HelpRequestIds,
        Guid FirstElderlyId,
        Guid SecondElderlyId,
        Guid DeletedElderlyId,
        Guid FirstCheckInId,
        Guid FirstSecondCheckInId,
        Guid SecondElderlyCheckInId,
        Guid DeletedCheckInId,
        Guid FirstSosId,
        Guid SecondElderlySosId,
        Guid DeletedSosId,
        Guid FirstHelpRequestId,
        Guid SecondElderlyHelpRequestId,
        Guid DeletedHelpRequestId);

    private sealed class F1PostgresFactAttribute : FactAttribute
    {
        public F1PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip = "F1 PostgreSQL proof is skipped. Set " +
                       "ConnectionStrings__IdentityIntegrationDatabase to a local " +
                       "PostgreSQL instance to run it against the disposable " +
                       "a uniquely named local disposable target.";
            }
        }
    }
}

[CollectionDefinition("F1ElderlyFilterVerification", DisableParallelization = true)]
public sealed class F1ElderlyFilterVerificationCollection
{
}
