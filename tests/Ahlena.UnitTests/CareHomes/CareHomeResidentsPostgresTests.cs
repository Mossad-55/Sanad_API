using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Families.Application.CareRecords;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// Portal second pass S1-A: relational proof for the care-home resident read
/// (<c>GET /api/v1/care-homes/residents/mine</c>). The production controller
/// runs against a validated disposable PostgreSQL target so a non-translatable
/// predicate fails loudly instead of silently passing on InMemory.
/// <para>
/// The target uses the credentials from the local loopback PostgreSQL named by
/// <c>ConnectionStrings__IdentityIntegrationDatabase</c>, but replaces the
/// database with a unique nonce-bearing name that is verified absent before
/// creation. This class never connects to the source database itself or to the
/// persistent <c>AhlenaDb</c>. It applies the committed Families and CareHomes
/// migrations, seeds minimal facility/booking/dependent rows, deletes them by
/// ID, and deletes only the database it created. When the base connection
/// string is absent the live facts skip.
/// </para>
/// <para>
/// The three offline translation facts run without a database and pin the
/// exact LINQ shapes the controller composes, so a regression back to a
/// <c>.Value</c>-peeling expression fails in every developer run.
/// </para>
/// </summary>
[Collection("CareHomeResidentsVerification")]
public sealed class CareHomeResidentsPostgresTests : IAsyncLifetime
{
    private const string ConnectionStringVariable =
        "ConnectionStrings__IdentityIntegrationDatabase";

    private static readonly string ExpectedDatabase =
        "AhlenaResidentsVerify_" + Guid.NewGuid().ToString("N");

    private static readonly DateTime FixedUtcNow = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly StayStart = new(2031, 1, 2);

    private static readonly DateOnly FixedToday = new(2026, 10, 25);

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
        // process created it; cleanup can never target AhlenaDb or any
        // pre-existing named database.
        await using FamiliesDbContext db = CreateFamiliesDb(_migratedFor);
        await db.Database.EnsureDeletedAsync();
        _createdTarget = false;
        _migratedFor = null;
    }

    // ======================= offline translation facts =======================

    [Fact]
    public void MappedStrongIdFilter_TranslatesOnNpgsql()
    {
        using FamiliesDbContext db = CreateFamiliesDb(ProbeConnectionString);
        List<ElderlyId> residentIds = [ElderlyId.New()];

        IQueryable<ElderlyId> query = db.Elderlies.AsNoTracking()
            .Where(e => residentIds.Contains(e.Id))
            .Select(e => e.Id);

        string sql = query.ToQueryString();
        Assert.Contains("families.elderlies", sql, StringComparison.OrdinalIgnoreCase);
        Assert.True(sql.Contains("= ANY", StringComparison.Ordinal), sql);
    }

    [Fact]
    public void MappedNameSearchAndOrder_TranslateOnNpgsql()
    {
        using FamiliesDbContext db = CreateFamiliesDb(ProbeConnectionString);
        string pattern = "%needle%";
        string escape = "\\";

        IQueryable<ElderlyId> query = db.Elderlies.AsNoTracking()
            .Where(e =>
                EF.Functions.ILike(e.ArabicFullName, pattern, escape) ||
                EF.Functions.ILike(e.EnglishFullName, pattern, escape))
            .OrderBy(e => e.ArabicFullName)
            .ThenBy(e => e.Id)
            .Select(e => e.Id);

        string sql = query.ToQueryString();
        Assert.Contains("ILIKE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ESCAPE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arabic_full_name", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyValueMemberExpressions_AreRejectedByNpgsql()
    {
        // Permanent regression witnesses for exactly what PostgreSQL refuses:
        // the original resident predicate and both name search/order members.
        // If any of these ever starts translating (or a reversion lands), the
        // corresponding controller shape must be re-derived deliberately.
        using FamiliesDbContext db = CreateFamiliesDb(ProbeConnectionString);
        Guid[] guids = [Guid.NewGuid()];
        string lowered = "x";

        IQueryable<ElderlyId> byValueContains = db.Elderlies.AsNoTracking()
            .Where(e => guids.Contains(e.Id.Value))
            .Select(e => e.Id);

        IQueryable<ElderlyId> byValueNameSearch = db.Elderlies.AsNoTracking()
            .Where(e =>
                e.ArabicFullName.Value.ToLower().Contains(lowered) ||
                e.EnglishFullName.Value.ToLower().Contains(lowered))
            .Select(e => e.Id);

        IQueryable<ElderlyId> byValueNameOrder = db.Elderlies.AsNoTracking()
            .OrderBy(e => e.ArabicFullName.Value)
            .Select(e => e.Id);

        AssertCannotTranslate(byValueContains);
        AssertCannotTranslate(byValueNameSearch);
        AssertCannotTranslate(byValueNameOrder);
    }

    // ========================== live relational facts =========================

    [ResidentsPostgresFact]
    public async Task DefaultRead_ReturnsOnlyAcceptedResidents_OfOwnFacility()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        Seed seed = await SeedAsync(connectionString);

        try
        {
            await using FamiliesDbContext families = CreateFamiliesDb(connectionString);
            await using CareHomesDbContext careHomes = CreateCareHomesDb(connectionString);

            List<Seed.ResidentRow> rowsA = await ListResidentsAsync(seed.OwnerA, families, careHomes);
            Assert.Equal(5, rowsA.Count);
            Assert.Equal(
                new[]
                {
                    seed.ResidentNames.AhmadArabic,
                    seed.ResidentNames.BasharArabic,
                    "لمى",
                    "نور",
                    "هيا",
                },
                // The stored/deterministic order itself is pinned by the
                // pagination fact; here the set identity is what matters, so
                // both sides are compared in ordinal order and the database's
                // Arabic collation cannot leak into this assertion.
                rowsA.Select(r => r.ArabicFullName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
            Assert.All(rowsA, r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.EnglishFullName));
                Assert.DoesNotContain("Kamal", r.EnglishFullName);
                Assert.DoesNotContain("Baraa", r.EnglishFullName);
            });

            // The second owner only sees the resident booked at their own
            // facility: all facility-A rows are foreign scope to them.
            List<Seed.ResidentRow> rowsB = await ListResidentsAsync(seed.OwnerB, families, careHomes);
            Seed.ResidentRow rowB = Assert.Single(rowsB);
            Assert.Equal(seed.ResidentNames.BaraaEnglish, rowB.EnglishFullName);
        }
        finally
        {
            await CleanupAsync(connectionString, seed);
        }
    }

    [ResidentsPostgresFact]
    public async Task MappedNameSearch_TrimsWhitespace_AndMatchesMetacharactersLiterally()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        Seed seed = await SeedAsync(connectionString);

        try
        {
            await using FamiliesDbContext families = CreateFamiliesDb(connectionString);
            await using CareHomesDbContext careHomes = CreateCareHomesDb(connectionString);

            // English match, typed in the wrong case: only the resident whose
            // English name carries "ahmad" matches.
            List<Seed.ResidentRow> englishMatch = await ListResidentsAsync(
                seed.OwnerA, families, careHomes, search: "AHMAD");
            Seed.ResidentRow matched = Assert.Single(englishMatch);
            Assert.Equal(seed.ResidentNames.AhmadEnglish, matched.EnglishFullName);

            // Arabic match: only the resident whose Arabic name carries
            // "بشار" matches.
            List<Seed.ResidentRow> arabicMatch = await ListResidentsAsync(
                seed.OwnerA, families, careHomes, search: "بشار");
            Assert.Single(arabicMatch);
            Assert.Equal(seed.ResidentNames.BasharArabic, arabicMatch[0].ArabicFullName);

            // Whitespace trim: surrounding spaces are trimmed, so the
            // needle behaves exactly like the trimmed text — without the
            // trim, the embedded-space pattern "%  ahmad  %" would match
            // nothing.
            List<Seed.ResidentRow> trimmedMatch = await ListResidentsAsync(
                seed.OwnerA, families, careHomes, search: "  ahmad  ");
            Seed.ResidentRow trimmed = Assert.Single(trimmedMatch);
            Assert.Equal(seed.ResidentNames.AhmadEnglish, trimmed.EnglishFullName);

            // Literal matching (regression witnesses against the previous
            // stripping behavior, which removed "%"/"_"/"\" and thereby
            // changed or disabled the filter):
            // "%" once stripped left the needle empty, which applied NO
            // filter and returned every resident; literal semantics return
            // only the name that actually contains '%'.
            List<Seed.ResidentRow> percentMatch = await ListResidentsAsync(
                seed.OwnerA, families, careHomes, search: "%");
            Seed.ResidentRow percent = Assert.Single(percentMatch);
            Assert.Equal("Nour%Adel", percent.EnglishFullName);

            // "_" as a wildcard would match every multi-character name;
            // literal semantics return only the name containing '_'.
            List<Seed.ResidentRow> underscoreMatch = await ListResidentsAsync(
                seed.OwnerA, families, careHomes, search: "_");
            Seed.ResidentRow underscore = Assert.Single(underscoreMatch);
            Assert.Equal("Huda_Salem", underscore.EnglishFullName);

            // "\" once stripped left the needle empty (no filter, all
            // residents returned); literal semantics return only the name
            // containing the backslash.
            List<Seed.ResidentRow> backslashMatch = await ListResidentsAsync(
                seed.OwnerA, families, careHomes, search: "\\");
            Seed.ResidentRow backslash = Assert.Single(backslashMatch);
            Assert.Equal("Leila\\Omar", backslash.EnglishFullName);

            // A compound needle keeps its metacharacters literal: "r%d" is
            // the three characters r, %, d — not the wildcard sequence
            // r-anything-d. No resident contains it.
            List<Seed.ResidentRow> compoundNoMatch = await ListResidentsAsync(
                seed.OwnerA, families, careHomes, search: "r%d");
            Assert.Empty(compoundNoMatch);
        }
        finally
        {
            await CleanupAsync(connectionString, seed);
        }
    }

    [ResidentsPostgresFact]
    public async Task Pagination_SplitsResidentsByMappedNameOrder_Stably()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        Seed seed = await SeedAsync(connectionString);

        try
        {
            await using FamiliesDbContext families = CreateFamiliesDb(connectionString);
            await using CareHomesDbContext careHomes = CreateCareHomesDb(connectionString);

            List<Seed.ResidentRow> page1 = await ListResidentsAsync(
                seed.OwnerA, families, careHomes, page: 1, pageSize: 1);
            List<Seed.ResidentRow> page2 = await ListResidentsAsync(
                seed.OwnerA, families, careHomes, page: 2, pageSize: 1);

            Seed.ResidentRow first = Assert.Single(page1);
            Seed.ResidentRow second = Assert.Single(page2);

            // Mapped-name ordering: the Ahmad resident precedes the Bashar
            // resident, and the split concatenation is stable across calls.
            Assert.Equal(seed.ResidentNames.AhmadEnglish, first.EnglishFullName);
            Assert.Equal(seed.ResidentNames.BasharEnglish, second.EnglishFullName);

            List<Seed.ResidentRow> page1Again = await ListResidentsAsync(
                seed.OwnerA, families, careHomes, page: 1, pageSize: 1);
            Assert.Equal(first.DependentId, page1Again[0].DependentId);
        }
        finally
        {
            await CleanupAsync(connectionString, seed);
        }
    }

    [ResidentsPostgresFact]
    public async Task SearchWithNoMatches_ReturnsEmptyPageWithZeroTotal()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        Seed seed = await SeedAsync(connectionString);

        try
        {
            await using FamiliesDbContext families = CreateFamiliesDb(connectionString);
            await using CareHomesDbContext careHomes = CreateCareHomesDb(connectionString);

            IActionResult result = await CreateController(seed.OwnerA, families, careHomes)
                .ListResidents(search: "nosuchname", 1, 20, default);

            var page = Assert.IsType<CareRecordsPage<CareHomeResidentResponse>>(
                Assert.IsType<OkObjectResult>(result).Value);
            Assert.Empty(page.Items);
            Assert.Equal(0, page.TotalCount);
        }
        finally
        {
            await CleanupAsync(connectionString, seed);
        }
    }

    [ResidentsPostgresFact]
    public async Task OversizedSearchAndBadPaging_ReturnCoded400()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);
        Seed seed = await SeedAsync(connectionString);

        try
        {
            await using FamiliesDbContext families = CreateFamiliesDb(connectionString);
            await using CareHomesDbContext careHomes = CreateCareHomesDb(connectionString);

            BadRequestObjectResult oversized = Assert.IsType<BadRequestObjectResult>(
                await CreateController(seed.OwnerA, families, careHomes)
                    .ListResidents(search: new string('x', 101), 1, 20, default));
            var problem = Assert.IsType<ProblemDetails>(oversized.Value);
            Assert.Equal("Families.CareRecords.InvalidQuery", problem.Extensions["code"]);

            BadRequestObjectResult badPage = Assert.IsType<BadRequestObjectResult>(
                await CreateController(seed.OwnerA, families, careHomes)
                    .ListResidents(search: null, 0, 20, default));
            var badPageProblem = Assert.IsType<ProblemDetails>(badPage.Value);
            Assert.Equal("Families.CareRecords.InvalidQuery", badPageProblem.Extensions["code"]);
        }
        finally
        {
            await CleanupAsync(connectionString, seed);
        }
    }

    // ================================ helpers ================================

    private static string ProbeConnectionString =>
        "Host=localhost;Database=translation_probe_never_opened;Username=probe;Password=probe";

    private static async Task<List<Seed.ResidentRow>> ListResidentsAsync(
        UserId owner,
        FamiliesDbContext families,
        CareHomesDbContext careHomes,
        string? search = null,
        int page = 1,
        int pageSize = 20)
    {
        IActionResult result = await CreateController(owner, families, careHomes)
            .ListResidents(search, page, pageSize, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var shape = Assert.IsType<CareRecordsPage<CareHomeResidentResponse>>(ok.Value);
        Assert.Equal(page, shape.Page);

        return shape.Items.Select(item => new Seed.ResidentRow(
            item.DependentId,
            item.ArabicFullName,
            item.EnglishFullName)).ToList();
    }

    private static CareHomeCareRecordsController CreateController(
        UserId owner,
        FamiliesDbContext families,
        CareHomesDbContext careHomes)
    {
        // ListResidents never dispatches through MediatR; the sender only
        // serves the other care-record actions on this controller.
        var controller = new CareHomeCareRecordsController(
            new ThrowingSender(), careHomes, families);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Sub, owner.Value.ToString()),
                ], "test"))
            }
        };
        return controller;
    }

    private static async Task<Seed> SeedAsync(string connectionString)
    {
        await using FamiliesDbContext families = CreateFamiliesDb(connectionString);
        await using CareHomesDbContext careHomes = CreateCareHomesDb(connectionString);

        UserId ownerA = UserId.New();
        UserId ownerB = UserId.New();

        Family familyA = Family.Create(ownerA, "Residents facility-A family");
        Family familyB = Family.Create(ownerB, "Residents facility-B family");

        // Five dependents of family A with an ACCEPTED booking at facility A
        // (the visible residents): two ordinary names, three carrying literal
        // LIKE metacharacters ("%", "_", "\") so the search cases can
        // distinguish literal matching from wildcard/stripping semantics, and
        // one (Kamal) whose booking at facility A was never accepted (stays
        // PendingPayment, must stay excluded). Facility B has its own accepted
        // resident (Baraa). Arabic names sort أحمد < بشار < هيا < لمى < نور.
        Elderly ahmad = Elderly.Create(
            ownerA, UserId.New(), familyA.Id, FamilyRelationshipType.Father,
            FullName.Create("أحمد"), FullName.Create("Ahmad Ali"),
            Gender.Male, new DateOnly(1948, 1, 1), FixedToday);
        Elderly bashar = Elderly.Create(
            ownerA, UserId.New(), familyA.Id, FamilyRelationshipType.Mother,
            FullName.Create("بشار"), FullName.Create("Bashar Omar"),
            Gender.Male, new DateOnly(1949, 1, 1), FixedToday);
        Elderly percent = Elderly.Create(
            ownerA, UserId.New(), familyA.Id, FamilyRelationshipType.Father,
            FullName.Create("نور"), FullName.Create("Nour%Adel"),
            Gender.Female, new DateOnly(1952, 1, 1), FixedToday);
        Elderly underscore = Elderly.Create(
            ownerA, UserId.New(), familyA.Id, FamilyRelationshipType.Mother,
            FullName.Create("هيا"), FullName.Create("Huda_Salem"),
            Gender.Female, new DateOnly(1953, 1, 1), FixedToday);
        Elderly backslash = Elderly.Create(
            ownerA, UserId.New(), familyA.Id, FamilyRelationshipType.Father,
            FullName.Create("لمى"), FullName.Create("Leila\\Omar"),
            Gender.Female, new DateOnly(1954, 1, 1), FixedToday);
        Elderly kamal = Elderly.Create(
            ownerA, UserId.New(), familyA.Id, FamilyRelationshipType.Father,
            FullName.Create("كمال"), FullName.Create("Kamal Samir"),
            Gender.Male, new DateOnly(1950, 1, 1), FixedToday);
        Elderly baraa = Elderly.Create(
            ownerB, UserId.New(), familyB.Id, FamilyRelationshipType.Father,
            FullName.Create("براء"), FullName.Create("Baraa Yousef"),
            Gender.Male, new DateOnly(1951, 1, 1), FixedToday);

        CareHomeFacility facilityA = CareHomeFacility.CreateDraft(ownerA, FixedUtcNow);
        CareHomeFacility facilityB = CareHomeFacility.CreateDraft(ownerB, FixedUtcNow);

        CareHomeBooking ahmadBooking = NewBooking(facilityA.Id, familyA.Id, ahmad.Id, paid: true);
        CareHomeBooking basharBooking = NewBooking(facilityA.Id, familyA.Id, bashar.Id, paid: true);
        CareHomeBooking percentBooking = NewBooking(facilityA.Id, familyA.Id, percent.Id, paid: true);
        CareHomeBooking underscoreBooking = NewBooking(facilityA.Id, familyA.Id, underscore.Id, paid: true);
        CareHomeBooking backslashBooking = NewBooking(facilityA.Id, familyA.Id, backslash.Id, paid: true);
        CareHomeBooking kamalPending = NewBooking(facilityA.Id, familyA.Id, kamal.Id, paid: false);
        CareHomeBooking baraaBooking = NewBooking(facilityB.Id, familyB.Id, baraa.Id, paid: true);

        families.Families.AddRange(familyA, familyB);
        families.Elderlies.AddRange(ahmad, bashar, percent, underscore, backslash, kamal, baraa);
        careHomes.Facilities.AddRange(facilityA, facilityB);
        careHomes.Bookings.AddRange(
            ahmadBooking, basharBooking, percentBooking, underscoreBooking,
            backslashBooking, kamalPending, baraaBooking);
        await families.SaveChangesAsync();
        await careHomes.SaveChangesAsync();

        // Readback prerequisites before the scenarios: the accepted set is
        // exactly what the reads below expect.
        Assert.Equal(5, await careHomes.Bookings.CountAsync(
            b => b.FacilityId == facilityA.Id && b.Status == CareHomeBookingStatus.Accepted));
        Assert.Equal(7, await families.Elderlies.CountAsync(
            e => e.Id == ahmad.Id || e.Id == bashar.Id || e.Id == percent.Id || e.Id == underscore.Id ||
                 e.Id == backslash.Id || e.Id == kamal.Id || e.Id == baraa.Id));

        return new Seed(
            OwnerA: ownerA,
            OwnerB: ownerB,
            FamilyIds: [familyA.Id, familyB.Id],
            ElderlyIds: [ahmad.Id, bashar.Id, percent.Id, underscore.Id, backslash.Id, kamal.Id, baraa.Id],
            BookingIds: [
                ahmadBooking.Id, basharBooking.Id, percentBooking.Id, underscoreBooking.Id,
                backslashBooking.Id, kamalPending.Id, baraaBooking.Id],
            FacilityIds: [facilityA.Id, facilityB.Id],
            ResidentNames: new KnownNames(
                AhmadArabic: ahmad.ArabicFullName.Value,
                AhmadEnglish: ahmad.EnglishFullName.Value,
                BasharArabic: bashar.ArabicFullName.Value,
                BasharEnglish: bashar.EnglishFullName.Value,
                BaraaEnglish: baraa.EnglishFullName.Value));
    }

    private static CareHomeBooking NewBooking(
        CareHomeId facilityId, FamilyId familyId, ElderlyId elderlyId, bool paid)
    {
        CareHomeBooking booking = CareHomeBooking.Create(
            facilityId, UserId.New(), familyId, elderlyId, Guid.NewGuid(),
            StayStart, 1000m, 100m, 50m, 4,
            "رعاية", "Care", 75, null, "Contact", null, null, null, FixedUtcNow);

        if (paid)
        {
            booking.MarkPaid(Random.Shared.NextInt64(1_000_000_000), FixedUtcNow);
            booking.Accept(FixedUtcNow);
        }

        return booking;
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
            await using FamiliesDbContext families = CreateFamiliesDb(connectionString);
            await families.Database.MigrateAsync();
            await using CareHomesDbContext careHomes = CreateCareHomesDb(connectionString);
            await careHomes.Database.MigrateAsync();
            _createdTarget = true;
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
                "Refusing to write: the residents verification source must be loopback:5432 PostgreSQL.");
        }

        builder.Database = ExpectedDatabase;
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
                "Refusing to write: the randomly named residents disposable target already exists; " +
                "no database was changed.");
        }
    }

    private static async Task CleanupAsync(string connectionString, Seed seed)
    {
        await using CareHomesDbContext careHomes = CreateCareHomesDb(connectionString);
        await careHomes.Bookings.Where(b => seed.BookingIds.Contains(b.Id)).ExecuteDeleteAsync();
        await careHomes.Facilities.Where(f => seed.FacilityIds.Contains(f.Id)).ExecuteDeleteAsync();

        await using FamiliesDbContext families = CreateFamiliesDb(connectionString);
        await families.Elderlies.Where(e => seed.ElderlyIds.Contains(e.Id)).ExecuteDeleteAsync();
        await families.Families.Where(f => seed.FamilyIds.Contains(f.Id)).ExecuteDeleteAsync();
    }

    private static FamiliesDbContext CreateFamiliesDb(string connectionString) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseNpgsql(connectionString).Options);

    private static CareHomesDbContext CreateCareHomesDb(string connectionString) =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseNpgsql(connectionString).Options);

    private static void AssertCannotTranslate<T>(IQueryable<T> query)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => query.ToQueryString());
        Assert.Contains("could not be translated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record KnownNames(
        string AhmadArabic,
        string AhmadEnglish,
        string BasharArabic,
        string BasharEnglish,
        string BaraaEnglish);

    private sealed record Seed(
        UserId OwnerA,
        UserId OwnerB,
        FamilyId[] FamilyIds,
        ElderlyId[] ElderlyIds,
        Guid[] BookingIds,
        CareHomeId[] FacilityIds,
        KnownNames ResidentNames)
    {
        internal sealed record ResidentRow(Guid DependentId, string ArabicFullName, string EnglishFullName);
    }

    private sealed class ThrowingSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("ListResidents must not dispatch through MediatR.");

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException("ListResidents must not dispatch through MediatR.");

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("ListResidents must not dispatch through MediatR.");

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ResidentsPostgresFactAttribute : FactAttribute
    {
        public ResidentsPostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip = "Residents PostgreSQL proof is skipped. Set " +
                       ConnectionStringVariable + " to a local PostgreSQL " +
                       "instance to run it against a fresh disposable target.";
            }
        }
    }
}

[CollectionDefinition("CareHomeResidentsVerification", DisableParallelization = true)]
public sealed class CareHomeResidentsVerificationCollection
{
}
