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

    private static CaregiversDbContext CreateInMemoryCaregiversDbContext()
    {
        DbContextOptions<CaregiversDbContext> options =
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CaregiversDbContext(options);
    }
}
