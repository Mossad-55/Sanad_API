using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

// PORTAL-04: matched/unmatched Identity behavior of the REAL production
// list/count SQL on an isolated SQLite attached-schema fixture
// (caregivers.caregivers + identity.users). This proves supported
// LEFT JOIN/reader/filter/paging semantics only; it is NOT PostgreSQL
// execution proof. No API host, no real provider, no Production target.
public sealed class CaregiverIdentityIntegrityTests
{
    [Fact]
    public async Task List_ShouldReturnAllCaregivers_IncludingUnmatchedIdentities()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);

            IReadOnlyList<AdminCaregiverListItem> items =
                await db.GetAdminCaregiversAsync(1, 10, null, null);

            Assert.Equal(5, items.Count);
        }
    }

    [Fact]
    public async Task List_ShouldReturnNullEnrichment_ForMissingIdentityUsers()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);

            IReadOnlyList<AdminCaregiverListItem> items =
                await db.GetAdminCaregiversAsync(1, 10, null, null);

            AdminCaregiverListItem orphan = items.Single(
                i => i.CaregiverId == seeded.Ids.OrphanCompanionCaregiver);

            // Exact reported shape: row present, names/phone null (not filtered).
            Assert.Equal(seeded.Ids.OrphanCompanionCaregiver, orphan.CaregiverId);
            Assert.Equal(CaregiverIntegrityFixture.OrphanCompanionUserId, orphan.UserId);
            Assert.Null(orphan.ArabicFullName);
            Assert.Null(orphan.EnglishFullName);
            Assert.Null(orphan.PhoneNumber);

            AdminCaregiverListItem matched = items.Single(
                i => i.CaregiverId == seeded.Ids.MatchedMedicalCaregiver);

            Assert.Equal("Matched Medical", matched.EnglishFullName);
            Assert.Equal("+10000000001", matched.PhoneNumber);
        }
    }

    [Fact]
    public async Task Count_ShouldIncludeUnmatchedIdentities_AndMatchListTotal()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);

            int total = await db.CountAdminCaregiversAsync(null, null);
            IReadOnlyList<AdminCaregiverListItem> items =
                await db.GetAdminCaregiversAsync(1, 10, null, null);

            Assert.Equal(5, total);
            Assert.Equal(total, items.Count);
        }
    }

    [Fact]
    public async Task StatusFilter_ShouldApplyEquallyToListAndCount()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);

            // CaregiverStatus.Active = 4: the two matched caregivers.
            int total = await db.CountAdminCaregiversAsync(4, null);
            IReadOnlyList<AdminCaregiverListItem> items =
                await db.GetAdminCaregiversAsync(1, 10, 4, null);

            Assert.Equal(2, total);
            Assert.Equal(total, items.Count);
            Assert.All(items, i => Assert.Equal(CaregiverStatus.Active, i.Status));
            Assert.All(items, i => Assert.NotNull(i.EnglishFullName));
        }
    }

    [Fact]
    public async Task TypeFilter_ShouldKeepUnmatchedRows_WhenTypeMatches()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);

            // CaregiverType.Companion = 2: two matched + one orphan.
            int total = await db.CountAdminCaregiversAsync(null, 2);
            IReadOnlyList<AdminCaregiverListItem> items =
                await db.GetAdminCaregiversAsync(1, 10, null, 2);

            Assert.Equal(3, total);
            Assert.Equal(total, items.Count);
            Assert.All(items, i => Assert.Equal(CaregiverType.Companion, i.Type));
            Assert.Single(items, i => i.EnglishFullName is null);
        }
    }

    [Fact]
    public async Task Paging_ShouldReturnDeterministicWindows_InUpdatedDescOrder()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);

            IReadOnlyList<AdminCaregiverListItem> page1 =
                await db.GetAdminCaregiversAsync(1, 2, null, null);
            IReadOnlyList<AdminCaregiverListItem> page2 =
                await db.GetAdminCaregiversAsync(2, 2, null, null);
            IReadOnlyList<AdminCaregiverListItem> page3 =
                await db.GetAdminCaregiversAsync(3, 2, null, null);
            int total = await db.CountAdminCaregiversAsync(null, null);

            Assert.Equal(
                [seeded.Ids.MatchedMedicalCaregiver, seeded.Ids.OrphanCompanionCaregiver],
                page1.Select(i => i.CaregiverId).ToList());
            Assert.Equal(
                [seeded.Ids.WrongRoleCaregiver, seeded.Ids.SecondOrphanCaregiver],
                page2.Select(i => i.CaregiverId).ToList());
            Assert.Equal([seeded.Ids.MatchedCompanionCaregiver], page3.Select(i => i.CaregiverId).ToList());

            // Empty window still reports the full total (no silent count shift).
            IReadOnlyList<AdminCaregiverListItem> empty =
                await db.GetAdminCaregiversAsync(10, 10, null, null);
            Assert.Empty(empty);
            Assert.Equal(5, total);
        }
    }

    [Fact]
    public async Task Handler_ShouldReturnPagedList_WithTotalAndItems()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);

            var result = await new GetCaregiversAdminQueryHandler(db).Handle(
                new GetCaregiversAdminQuery(1, 10, null, null),
                default);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value.Page);
            Assert.Equal(10, result.Value.PageSize);
            Assert.Equal(5, result.Value.TotalCount);
            Assert.Equal(5, result.Value.Items.Count);
        }
    }

    [Fact]
    public async Task Handler_ShouldPropagateStatusAndTypeFilters()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);

            var result = await new GetCaregiversAdminQueryHandler(db).Handle(
                new GetCaregiversAdminQuery(
                    1,
                    10,
                    CaregiverStatus.PendingReview,
                    CaregiverType.Companion),
                default);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value.TotalCount);
            AdminCaregiverListItem single = Assert.Single(result.Value.Items);
            Assert.Equal(seeded.Ids.OrphanCompanionCaregiver, single.CaregiverId);
            Assert.Null(single.EnglishFullName);
        }
    }

    [Fact]
    public void QueryValidator_ShouldRejectInvalidPaging()
    {
        var validator = new GetCaregiversAdminQueryValidator();

        Assert.False(validator.Validate(new GetCaregiversAdminQuery(0, 10, null, null)).IsValid);
        Assert.False(validator.Validate(new GetCaregiversAdminQuery(1, 0, null, null)).IsValid);
        Assert.False(validator.Validate(new GetCaregiversAdminQuery(1, 101, null, null)).IsValid);
        Assert.True(validator.Validate(new GetCaregiversAdminQuery(1, 10, null, null)).IsValid);
    }

    [Fact]
    public async Task Detail_ShouldReturnProfile_WithoutAnyIdentityRow()
    {
        // The admin detail query reads the Caregivers aggregate only; it never
        // touches identity.users, so an orphaned reference still opens detail.
        using CaregiversDbContext db = CreateInMemoryCaregiversDbContext();

        UserId userId = UserId.New();
        var bootstrap = await new BootstrapCaregiverCommandHandler(db).Handle(
            new BootstrapCaregiverCommand(userId, CaregiverType.Companion),
            default);

        Assert.True(bootstrap.IsSuccess);

        var detail = await new GetCaregiverAdminDetailQueryHandler(db).Handle(
            new GetCaregiverAdminDetailQuery(bootstrap.Value.Id),
            default);

        Assert.True(detail.IsSuccess);
        Assert.Equal(userId, detail.Value.UserId);
    }

    // ---------------- W4: list display classes and the identityPresent flag ----------------
    //
    // The facts below reproduce list behavior for the row classes PORTAL-04
    // plus the integrity follow-up surfaced: missing identity, anonymized
    // identity, deactivated caregiver. Since the flag stage, the list projects
    // `IdentityPresent`, which means ONLY "an identity.users row was joined on
    // the configured query target during this read": it alone distinguishes
    // neither anonymization nor deactivation (both can join), and a false
    // value is not authoritative cross-target identity absence (wrong-id joins
    // and split-target deployments stay outside its meaning).

    private static readonly string SqliteInMemoryProvider = "Microsoft.EntityFrameworkCore.Sqlite";

    [Fact]
    public void ExtendedFixture_Guard_IsIsolatedInMemorySqlite_AndCannotReachAPersistentTarget()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeededWithIntegrityClasses();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);

            // Execution guard: the fixture context is SQLite bound to the
            // kept-open :memory: connection, so the production statements can
            // never reach a persistent (PostgreSQL/local SanadDb) target.
            Assert.Equal(SqliteInMemoryProvider, db.Database.ProviderName);
            string? connectionString = db.Database.GetConnectionString();
            Assert.NotNull(connectionString);
            Assert.Contains(":memory:", connectionString, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task List_ShouldRenderAnonymizedIdentityShape_WithPlaceholderNames_NotNulls()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeededWithIntegrityClasses();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);

            IReadOnlyList<AdminCaregiverListItem> items =
                await db.GetAdminCaregiversAsync(1, 10, null, null);

            // The anonymized row is listed like any other matched row and is
            // visibly NOT the missing-identity shape: enrichment exists, but
            // as the placeholder pair today's anonymization writes.
            AdminCaregiverListItem anonymized = items.Single(
                i => i.CaregiverId == CaregiverIntegrityFixture.AnonymizedCaregiverId);

            Assert.Equal(CaregiverIntegrityFixture.AnonymizedUserId, anonymized.UserId);
            Assert.Equal(CaregiverIntegrityFixture.AnonymizedArabicName, anonymized.ArabicFullName);
            Assert.Equal(CaregiverIntegrityFixture.AnonymizedEnglishName, anonymized.EnglishFullName);
            Assert.Equal(CaregiverIntegrityFixture.AnonymizedPhone, anonymized.PhoneNumber);
            Assert.Equal(CaregiverStatus.Active, anonymized.Status);

            // The anonymized identity row EXISTS and joined: the flag is true.
            // The flag alone does not reveal the row is anonymized.
            Assert.True(anonymized.IdentityPresent);

            // Contrast within the same list: the missing-identity row still
            // shows the null-enrichment shape, and it is the only shape the
            // flag alone can separate (no joined row -> false).
            AdminCaregiverListItem orphan = items.Single(
                i => i.CaregiverId == seeded.BaseIds.OrphanCompanionCaregiver);
            Assert.Null(orphan.ArabicFullName);
            Assert.Null(orphan.EnglishFullName);
            Assert.Null(orphan.PhoneNumber);
            Assert.False(orphan.IdentityPresent);
        }
    }

    [Fact]
    public async Task StatusFilter7_ShouldReturnDeactivatedCaregiver_WithIntactNames_AndMatchCount()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeededWithIntegrityClasses();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            var handler = new GetCaregiversAdminQueryHandler(db);

            // CaregiverStatus.Deactivated = 7: deactivation is the caregiver
            // row's own status discriminator; the identity enrichment stays.
            var result = await handler.Handle(
                new GetCaregiversAdminQuery(1, 10, CaregiverStatus.Deactivated, null),
                default);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value.TotalCount);
            AdminCaregiverListItem deactivated = Assert.Single(result.Value.Items);
            Assert.Equal(CaregiverIntegrityFixture.DeactivatedCaregiverId, deactivated.CaregiverId);
            Assert.Equal(CaregiverIntegrityFixture.DeactivatedUserId, deactivated.UserId);
            Assert.Equal("اسم معطل", deactivated.ArabicFullName);
            Assert.Equal("Deactivated User", deactivated.EnglishFullName);
            Assert.Equal(CaregiverStatus.Deactivated, deactivated.Status);
            // Deactivation is a caregiver-side status; the identity row joined,
            // so the flag is true — the flag does not mark deactivation.
            Assert.True(deactivated.IdentityPresent);

            // The count query applies the same filter: list total == count total.
            int count = await db.CountAdminCaregiversAsync((int)CaregiverStatus.Deactivated, null);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task Totals_ShouldIncludeAnonymizedAndDeactivatedRows()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeededWithIntegrityClasses();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            var handler = new GetCaregiversAdminQueryHandler(db);

            var result = await handler.Handle(
                new GetCaregiversAdminQuery(1, 100, null, null),
                default);

            Assert.True(result.IsSuccess);
            int total = await db.CountAdminCaregiversAsync(null, null);

            // 5 core rows + 1 anonymized-shape + 1 deactivated: none is
            // silently filtered from the list or the count.
            Assert.Equal(7, result.Value.TotalCount);
            Assert.Equal(7, total);
            Assert.Equal(7, result.Value.Items.Count);
        }
    }

    [Fact]
    public async Task IdentityPresent_ShouldMeanJoinedRowOnly_ExactlyTwoRowsFalse()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeededWithIntegrityClasses();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            var handler = new GetCaregiversAdminQueryHandler(db);

            var result = await handler.Handle(
                new GetCaregiversAdminQuery(1, 100, null, null),
                default);

            Assert.True(result.IsSuccess);

            // Narrow semantics pinned: the flag tracks ONLY the LEFT JOIN
            // result on the configured query target. The two orphan rows
            // (no identity.users row for their user_id) are false; every
            // joined row — matched, anonymized-shape and deactivated — is
            // true, regardless of its cause or caregiver status.
            List<AdminCaregiverListItem> falseRows =
                result.Value.Items.Where(i => !i.IdentityPresent).ToList();

            Assert.Equal(2, falseRows.Count);
            Assert.All(falseRows, row =>
                Assert.Contains(
                    row.CaregiverId,
                    new[]
                    {
                        seeded.BaseIds.OrphanCompanionCaregiver,
                        seeded.BaseIds.SecondOrphanCaregiver
                    }));

            AdminCaregiverListItem anonymized = result.Value.Items.Single(
                i => i.CaregiverId == CaregiverIntegrityFixture.AnonymizedCaregiverId);
            AdminCaregiverListItem deactivated = result.Value.Items.Single(
                i => i.CaregiverId == CaregiverIntegrityFixture.DeactivatedCaregiverId);
            AdminCaregiverListItem matched = result.Value.Items.Single(
                i => i.CaregiverId == seeded.BaseIds.MatchedMedicalCaregiver);
            Assert.True(anonymized.IdentityPresent);
            Assert.True(deactivated.IdentityPresent);
            Assert.True(matched.IdentityPresent);
        }
    }

    private static CaregiversDbContext CreateInMemoryCaregiversDbContext()
    {
        DbContextOptions<CaregiversDbContext> options =
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CaregiversDbContext(options);
    }
}
