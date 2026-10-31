using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.Families;

/// <summary>
/// Dedicated dependent (elderly) name+id lookup for the Admin operational
/// surface. Every case runs against a relational (SQLite) Families store on
/// purpose: the deleted-family exclusion, the count, the clamped SQL-side
/// paging, the deterministic name-then-id ordering, the overflow-safe skip and
/// the narrow projection are exactly the parts a relational translation can
/// reject, and the lookup must translate there too rather than materializing
/// the register to filter it in memory.
/// <para>
/// The <em>search</em> predicate is deliberately not exercised here: it uses the
/// 3-argument ILIKE overload that only PostgreSQL translates, and its SQL shape
/// plus the pattern-escaping rules of both lookup routes are pinned by
/// <see cref="AdminElderlyLookupProviderTests"/> through the real Npgsql
/// mapping. Presenting an in-process claim for it would not be a provider
/// proof. The projection is asserted to publish only the id and the two names.
/// </para>
/// </summary>
public sealed class AdminElderlyLookupQueryTests
{
    [Fact]
    public async Task Lookup_blank_search_returns_the_live_page_and_never_errors()
    {
        await using TestElderlyDb db = await TestElderlyDb.CreateAsync();
        await db.SeedAsync();

        var handler = new LookupAdminElderliesQueryHandler(db.Context);

        var blank = await handler.Handle(new(Search: "   "), default);
        var absent = await handler.Handle(new(Search: null), default);

        Assert.True(blank.IsSuccess);

        // Seed state: three dependents live — one of them sits in a family that
        // is only deleted later, in the dedicated exclusion case below.
        Assert.Equal(3, blank.Value.TotalCount);
        Assert.Equal(3, blank.Value.Items.Count);
        Assert.Equal(blank.Value.TotalCount, absent.Value.TotalCount);
    }

    [Fact]
    public async Task Lookup_pages_stably_and_never_omits_or_repeats_a_dependent()
    {
        await using TestElderlyDb db = await TestElderlyDb.CreateAsync();
        await db.SeedAsync();

        var handler = new LookupAdminElderliesQueryHandler(db.Context);
        var first = await handler.Handle(new(Page: 1, PageSize: 1), default);
        var second = await handler.Handle(new(Page: 2, PageSize: 1), default);
        var third = await handler.Handle(new(Page: 3, PageSize: 1), default);
        var beyond = await handler.Handle(new(Page: 4, PageSize: 1), default);

        // totalCount is the filtered total, never the page size, and it stays
        // the same on every page including the one past the last.
        Assert.Equal(3, first.Value.TotalCount);
        Assert.Equal(3, first.Value.TotalPages);
        Assert.Single(first.Value.Items);
        Assert.Equal(3, second.Value.TotalCount);
        Assert.Single(second.Value.Items);
        Assert.Single(third.Value.Items);
        Assert.Empty(beyond.Value.Items);
        Assert.Equal(3, beyond.Value.TotalCount);

        Guid[] paged =
        [
            .. first.Value.Items.Select(item => item.Id),
            .. second.Value.Items.Select(item => item.Id),
            .. third.Value.Items.Select(item => item.Id)
        ];
        Assert.Equal(3, paged.Distinct().Count());
    }

    [Fact]
    public async Task Lookup_orders_by_the_two_names_and_clamps_paging_without_error()
    {
        await using TestElderlyDb db = await TestElderlyDb.CreateAsync();
        await db.SeedAsync();

        var handler = new LookupAdminElderliesQueryHandler(db.Context);

        var defaults = await handler.Handle(new(Page: 0, PageSize: 0), default);
        var negative = await handler.Handle(new(Page: -4, PageSize: -10), default);
        var oversized = await handler.Handle(new(PageSize: 500), default);

        Assert.Equal(1, defaults.Value.Page);
        Assert.Equal(20, defaults.Value.PageSize);
        Assert.Equal(1, negative.Value.Page);
        Assert.Equal(20, negative.Value.PageSize);
        Assert.Equal(100, oversized.Value.PageSize);

        // The Guid tie-breaker makes the page deterministic; the stored column
        // order is the Arabic name first, then the English name.
        Assert.Equal(
            defaults.Value.Items.Select(item => item.ArabicFullName).OrderBy(x => x, StringComparer.Ordinal),
            defaults.Value.Items.Select(item => item.ArabicFullName));
    }

    [Fact]
    public async Task Lookup_saturates_a_very_large_page_instead_of_overflowing()
    {
        await using TestElderlyDb db = await TestElderlyDb.CreateAsync();
        await db.SeedAsync();

        var result = await new LookupAdminElderliesQueryHandler(db.Context)
            .Handle(new(Search: null, Page: int.MaxValue, PageSize: 100), default);

        // (page - 1) * pageSize must not wrap to a negative OFFSET: the last
        // pageable offset is asked for, the page is simply empty.
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(3, result.Value.TotalCount);
        Assert.Equal(int.MaxValue, result.Value.Page);
    }

    [Fact]
    public async Task Lookup_excludes_dependents_of_a_deleted_family_and_never_deletes_them()
    {
        await using TestElderlyDb db = await TestElderlyDb.CreateAsync();
        Seeded seeded = await db.SeedAsync();
        await db.DeleteFamilyAsync(seeded.DeletedFamilyId);

        var all = await new LookupAdminElderliesQueryHandler(db.Context)
            .Handle(new(PageSize: 100), default);

        Assert.Equal(2, all.Value.TotalCount);
        Assert.DoesNotContain(
            seeded.DeletedDependentId,
            all.Value.Items.Select(item => item.Id));

        // The row and its name are still stored: the exclusion is a read rule,
        // never a delete.
        List<Elderly> stored = await db.Context.Elderlies
            .AsNoTracking()
            .ToListAsync();
        Assert.Contains(
            stored,
            elderly => elderly.Id.Value == seeded.DeletedDependentId);
    }

    [Fact]
    public async Task Lookup_publishes_the_id_and_the_two_names_only()
    {
        await using TestElderlyDb db = await TestElderlyDb.CreateAsync();
        Seeded seeded = await db.SeedAsync();

        var result = await new LookupAdminElderliesQueryHandler(db.Context)
            .Handle(new(PageSize: 100), default);

        AdminElderlyLookupItem item = Assert.Single(
            result.Value.Items,
            row => row.Id == seeded.MansourDependentId);
        Assert.Equal("الجد منصور", item.ArabicFullName);
        Assert.Equal("Mansour Grandfather", item.EnglishFullName);

        using JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(item));
        string[] keys = [.. json.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(["ArabicFullName", "EnglishFullName", "Id"], keys);
        Assert.Equal(seeded.MansourDependentId, json.RootElement.GetProperty("Id").GetGuid());
    }

    private sealed class TestElderlyDb : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestElderlyDb(SqliteConnection connection, FamiliesDbContext context)
        {
            _connection = connection;
            Context = context;
        }

        public FamiliesDbContext Context { get; }

        public static async Task<TestElderlyDb> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var context = new FamiliesDbContext(
                new DbContextOptionsBuilder<FamiliesDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await context.Database.EnsureCreatedAsync();
            return new TestElderlyDb(connection, context);
        }

        public async Task<Seeded> SeedAsync()
        {
            UserId liveOwner = UserId.New();
            UserId deletedOwner = UserId.New();
            Family liveFamily = Family.Create(liveOwner, "Lookup live family");
            Family deletedFamily = Family.Create(deletedOwner, "Lookup deleted family");
            DateOnly today = new(2026, 1, 1);

            Elderly mansour = Elderly.Create(
                liveOwner,
                UserId.New(),
                liveFamily.Id,
                FamilyRelationshipType.Father,
                FullName.Create("الجد منصور"),
                FullName.Create("Mansour Grandfather"),
                Gender.Male,
                new DateOnly(1950, 1, 1),
                today);
            Elderly other = Elderly.Create(
                liveOwner,
                UserId.New(),
                liveFamily.Id,
                FamilyRelationshipType.Mother,
                FullName.Create("الجدة زينب"),
                FullName.Create("Zainab Grandmother"),
                Gender.Female,
                new DateOnly(1952, 2, 2),
                today);
            Elderly deleted = Elderly.Create(
                deletedOwner,
                UserId.New(),
                deletedFamily.Id,
                FamilyRelationshipType.Father,
                FullName.Create("محذوف deleted-dependent"),
                FullName.Create("deleted-dependent"),
                Gender.Male,
                new DateOnly(1951, 3, 3),
                today);

            Context.AddRange(liveFamily, deletedFamily, mansour, other, deleted);
            await Context.SaveChangesAsync();

            return new Seeded(
                liveFamily.Id.Value,
                deletedFamily.Id.Value,
                mansour.Id.Value,
                other.Id.Value,
                deleted.Id.Value);
        }

        public async Task DeleteFamilyAsync(Guid familyId)
        {
            Family family = await Context.Families
                .SingleAsync(f => f.Id == new FamilyId(familyId));
            family.MarkDeleted("lookup verification", null);
            await Context.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed record Seeded(
        Guid LiveFamilyId,
        Guid DeletedFamilyId,
        Guid MansourDependentId,
        Guid ArabicNamedDependentId,
        Guid DeletedDependentId);
}
