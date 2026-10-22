using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

/// <summary>
/// SQL-level evidence for the caregiver discovery search over a real SQLite
/// engine (the bundled e_sqlite3, not the in-memory provider): the governorate
/// and city scope filters are EXISTS subqueries through the working-area
/// selections, they AND-combine with each other and with the name filter, and
/// every public surface publishes the avatar as the authorized photo path
/// instead of the stored Identity <c>avatar_url</c> key (refund decision lock
/// #32). The fixture models only the columns the two search statements touch,
/// with the exact EF-mapped physical column names; seeding is raw ADO with
/// Guid-typed parameters, mirroring production value storage.
/// </summary>
public sealed class CaregiverDiscoveryFilterSqlTests
{
    [Fact]
    public async Task Search_without_filters_returns_both_caregivers_with_path_or_null_avatars()
    {
        await using CaregiverDiscoverySqlFixture fixture = await CaregiverDiscoverySqlFixture.CreateAsync();
        CaregiverDiscoverySqlFixture.Seed cairo = await fixture.SeedCairoCaregiverAsync();
        await fixture.SeedGizaCaregiverAsync();

        Result<PagedResult<CaregiverSearchCardResponse>> result = await fixture.HandleAsync(
            new SearchCaregiversQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);

        CaregiverSearchCardResponse cairoCard =
            result.Value.Items.Single(item => item.Id == cairo.CaregiverId);
        Assert.Equal($"/api/v1/caregivers/{cairo.CaregiverId}/photo", cairoCard.AvatarUrl);
        Assert.DoesNotContain("private/", cairoCard.AvatarUrl);

        // The honest absent value: a caregiver with no stored avatar stays null.
        CaregiverSearchCardResponse gizaCard =
            result.Value.Items.Single(item => item.Id == fixture.GizaCaregiverId);
        Assert.Null(gizaCard.AvatarUrl);
    }

    [Fact]
    public async Task Search_with_a_governorate_filter_returns_only_that_governorates_caregiver()
    {
        await using CaregiverDiscoverySqlFixture fixture = await CaregiverDiscoverySqlFixture.CreateAsync();
        CaregiverDiscoverySqlFixture.Seed cairo = await fixture.SeedCairoCaregiverAsync();
        await fixture.SeedGizaCaregiverAsync();

        Result<PagedResult<CaregiverSearchCardResponse>> result = await fixture.HandleAsync(
            new SearchCaregiversQuery(GovernorateId: fixture.GovernorateA));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
        CaregiverSearchCardResponse card = Assert.Single(result.Value.Items);
        Assert.Equal(cairo.CaregiverId, card.Id);
    }

    [Fact]
    public async Task Search_with_a_city_filter_returns_only_that_citys_caregiver()
    {
        await using CaregiverDiscoverySqlFixture fixture = await CaregiverDiscoverySqlFixture.CreateAsync();
        await fixture.SeedCairoCaregiverAsync();
        await fixture.SeedGizaCaregiverAsync();

        Result<PagedResult<CaregiverSearchCardResponse>> result = await fixture.HandleAsync(
            new SearchCaregiversQuery(CityId: fixture.GizaCityId));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
        CaregiverSearchCardResponse card = Assert.Single(result.Value.Items);
        Assert.Equal(fixture.GizaCaregiverId, card.Id);
    }

    [Fact]
    public async Task Search_with_governorate_and_city_filters_AND_combines_the_scopes()
    {
        await using CaregiverDiscoverySqlFixture fixture = await CaregiverDiscoverySqlFixture.CreateAsync();
        CaregiverDiscoverySqlFixture.Seed cairo = await fixture.SeedCairoCaregiverAsync();
        await fixture.SeedGizaCaregiverAsync();

        // Both filters point inside the same caregiver's working areas: the
        // intersection stays.
        Result<PagedResult<CaregiverSearchCardResponse>> intersection = await fixture.HandleAsync(
            new SearchCaregiversQuery(GovernorateId: fixture.GovernorateA, CityId: fixture.CairoCityId));
        Assert.True(intersection.IsSuccess);
        CaregiverSearchCardResponse kept = Assert.Single(intersection.Value.Items);
        Assert.Equal(cairo.CaregiverId, kept.Id);

        // The same two scopes aimed across governorates contradict each other:
        // no caregiver satisfies both EXISTS guards.
        Result<PagedResult<CaregiverSearchCardResponse>> contradiction = await fixture.HandleAsync(
            new SearchCaregiversQuery(GovernorateId: fixture.GovernorateA, CityId: fixture.GizaCityId));
        Assert.True(contradiction.IsSuccess);
        Assert.Empty(contradiction.Value.Items);
        Assert.Equal(0, contradiction.Value.TotalCount);
    }

    [Fact]
    public async Task Search_with_a_non_matching_governorate_returns_an_empty_page()
    {
        await using CaregiverDiscoverySqlFixture fixture = await CaregiverDiscoverySqlFixture.CreateAsync();
        await fixture.SeedCairoCaregiverAsync();
        await fixture.SeedGizaCaregiverAsync();

        Result<PagedResult<CaregiverSearchCardResponse>> result = await fixture.HandleAsync(
            new SearchCaregiversQuery(GovernorateId: Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task Search_name_filter_matches_case_insensitively_on_both_names()
    {
        await using CaregiverDiscoverySqlFixture fixture = await CaregiverDiscoverySqlFixture.CreateAsync();
        CaregiverDiscoverySqlFixture.Seed cairo = await fixture.SeedCairoCaregiverAsync();
        await fixture.SeedGizaCaregiverAsync();

        // The cross-dialect form (lower(x) LIKE lower(y)) keeps the
        // PostgreSQL case-insensitive semantics and prepares on SQLite.
        Result<PagedResult<CaregiverSearchCardResponse>> byEnglish = await fixture.HandleAsync(
            new SearchCaregiversQuery(Search: "AMIRA"));
        Assert.True(byEnglish.IsSuccess);
        CaregiverSearchCardResponse englishCard = Assert.Single(byEnglish.Value.Items);
        Assert.Equal(cairo.CaregiverId, englishCard.Id);

        Result<PagedResult<CaregiverSearchCardResponse>> byArabic = await fixture.HandleAsync(
            new SearchCaregiversQuery(Search: "سامي"));
        Assert.True(byArabic.IsSuccess);
        CaregiverSearchCardResponse arabicCard = Assert.Single(byArabic.Value.Items);
        Assert.Equal(fixture.GizaCaregiverId, arabicCard.Id);
    }

    [Fact]
    public async Task Medical_recipients_search_publishes_the_authorized_photo_path_not_the_key()
    {
        await using CaregiverDiscoverySqlFixture fixture = await CaregiverDiscoverySqlFixture.CreateAsync();
        CaregiverDiscoverySqlFixture.Seed cairo = await fixture.SeedCairoCaregiverAsync();
        // The Giza caregiver is a Companion; only the Medical caregiver is a
        // medical-access recipient.
        await fixture.SeedGizaCaregiverAsync();

        (IReadOnlyList<MedicalCaregiverRecipientItem> Items, int TotalCount) result =
            await fixture.Db.SearchActiveMedicalCaregiversAsync(search: null, page: 1, pageSize: 20);

        Assert.Equal(1, result.TotalCount);
        MedicalCaregiverRecipientItem item = Assert.Single(result.Items);
        Assert.Equal(cairo.CaregiverId, item.CaregiverId.Value);
        Assert.Equal($"/api/v1/caregivers/{cairo.CaregiverId}/photo", item.AvatarUrl);
        Assert.DoesNotContain("private/", item.AvatarUrl);
    }
}

/// <summary>
/// A SQLite connection carrying the <c>caregivers</c> and <c>identity</c>
/// attached schemas whose physical columns match the EF mappings the discovery
/// search reads, plus the real <see cref="CaregiversDbContext"/> on top.
/// </summary>
internal sealed class CaregiverDiscoverySqlFixture : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    private CaregiverDiscoverySqlFixture(SqliteConnection connection, CaregiversDbContext db)
    {
        _connection = connection;
        Db = db;
    }

    public CaregiversDbContext Db { get; }

    private DateTime Now { get; } = DateTime.UtcNow;

    public Guid GovernorateA { get; private set; }
    public Guid GovernorateB { get; private set; }
    public Guid CairoCityId { get; private set; }
    public Guid GizaCityId { get; private set; }
    public Guid GizaCaregiverId { get; private set; }

    private Guid NursingSpecializationId { get; set; }

    internal sealed record Seed(Guid CaregiverId, Guid UserId);

    public static async Task<CaregiverDiscoverySqlFixture> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                """
                ATTACH DATABASE ':memory:' AS caregivers;
                ATTACH DATABASE ':memory:' AS identity;

                -- Physical column names mirror the EF mappings the executed
                -- search SQL reads. Owned-visibility booleans are required in
                -- production, so they are NOT NULL here; the fixture models
                -- only the columns the two statements touch.
                CREATE TABLE caregivers.caregivers (
                    id TEXT NOT NULL PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    type INTEGER NOT NULL,
                    status INTEGER NOT NULL,
                    availability INTEGER NOT NULL,
                    show_profile INTEGER NOT NULL,
                    average_rating NUMERIC NOT NULL,
                    reviews_count INTEGER NOT NULL,
                    medical_specialization_id TEXT NULL,
                    companion_specialization_id TEXT NULL,
                    medical_professional_title_id TEXT NULL,
                    medical_years_of_experience INTEGER NULL,
                    companion_years_of_experience INTEGER NULL,
                    companion_hourly_price NUMERIC NULL,
                    medical_eight_hour_shift_price NUMERIC NULL
                );
                CREATE TABLE caregivers.governorates (
                    id TEXT NOT NULL PRIMARY KEY,
                    arabic_name TEXT NOT NULL,
                    english_name TEXT NOT NULL,
                    is_active INTEGER NOT NULL,
                    created_on_utc TEXT NOT NULL,
                    updated_on_utc TEXT NOT NULL
                );
                CREATE TABLE caregivers.cities (
                    id TEXT NOT NULL PRIMARY KEY,
                    governorate_id TEXT NOT NULL,
                    arabic_name TEXT NOT NULL,
                    english_name TEXT NOT NULL,
                    is_active INTEGER NOT NULL,
                    created_on_utc TEXT NOT NULL,
                    updated_on_utc TEXT NOT NULL
                );
                CREATE TABLE caregivers.areas (
                    id TEXT NOT NULL PRIMARY KEY,
                    city_id TEXT NOT NULL,
                    arabic_name TEXT NOT NULL,
                    english_name TEXT NOT NULL,
                    is_active INTEGER NOT NULL,
                    created_on_utc TEXT NOT NULL,
                    updated_on_utc TEXT NOT NULL
                );
                CREATE TABLE caregivers.caregiver_area_selections (
                    caregiver_id TEXT NOT NULL,
                    area_id TEXT NOT NULL,
                    PRIMARY KEY (caregiver_id, area_id)
                );
                CREATE TABLE caregivers.specializations (
                    id TEXT NOT NULL PRIMARY KEY,
                    arabic_name TEXT NOT NULL,
                    english_name TEXT NOT NULL,
                    caregiver_type INTEGER NOT NULL,
                    is_active INTEGER NOT NULL,
                    created_on_utc TEXT NOT NULL,
                    updated_on_utc TEXT NOT NULL
                );
                CREATE TABLE caregivers.professional_titles (
                    id TEXT NOT NULL PRIMARY KEY,
                    arabic_name TEXT NOT NULL,
                    english_name TEXT NOT NULL,
                    is_active INTEGER NOT NULL,
                    created_on_utc TEXT NOT NULL,
                    updated_on_utc TEXT NOT NULL
                );
                CREATE TABLE identity.users (
                    id TEXT NOT NULL PRIMARY KEY,
                    arabic_full_name TEXT NOT NULL,
                    english_full_name TEXT NOT NULL,
                    gender INTEGER NULL,
                    avatar_url TEXT NULL
                );
                """;
            command.ExecuteNonQuery();
        }

        var fixture = new CaregiverDiscoverySqlFixture(
            connection,
            new CaregiversDbContext(
                new DbContextOptionsBuilder<CaregiversDbContext>()
                    .UseSqlite(connection)
                    .Options));

        await fixture.SeedLookupsAsync();
        return fixture;
    }

    private async Task SeedLookupsAsync()
    {
        GovernorateA = Guid.NewGuid();
        GovernorateB = Guid.NewGuid();
        NursingSpecializationId = Guid.NewGuid();

        await ExecAsync(
            "INSERT INTO caregivers.governorates (id, arabic_name, english_name, is_active, created_on_utc, updated_on_utc) " +
            "VALUES ($id, 'القاهرة', 'Cairo', 1, $now, $now);",
            Now, ("$id", GovernorateA));
        await ExecAsync(
            "INSERT INTO caregivers.governorates (id, arabic_name, english_name, is_active, created_on_utc, updated_on_utc) " +
            "VALUES ($id, 'الجيزة', 'Giza', 1, $now, $now);",
            Now, ("$id", GovernorateB));
        await ExecAsync(
            "INSERT INTO caregivers.specializations (id, arabic_name, english_name, caregiver_type, is_active, created_on_utc, updated_on_utc) " +
            "VALUES ($id, 'تمريض', 'Nursing', 1, 1, $now, $now);",
            Now, ("$id", NursingSpecializationId));
    }

    /// <summary>A Medical caregiver in governorate A, with a stored avatar key.</summary>
    public async Task<Seed> SeedCairoCaregiverAsync()
    {
        CairoCityId = Guid.NewGuid();

        await ExecAsync(
            "INSERT INTO caregivers.cities (id, governorate_id, arabic_name, english_name, is_active, created_on_utc, updated_on_utc) " +
            "VALUES ($id, $governorate, 'مدينة نصر', 'Nasr City', 1, $now, $now);",
            Now,
            ("$id", CairoCityId),
            ("$governorate", GovernorateA));

        Guid areaId = Guid.NewGuid();
        await ExecAsync(
            "INSERT INTO caregivers.areas (id, city_id, arabic_name, english_name, is_active, created_on_utc, updated_on_utc) " +
            "VALUES ($id, $city, 'عباس العقاد', 'Abbas El Akkad', 1, $now, $now);",
            Now,
            ("$id", areaId),
            ("$city", CairoCityId));

        Guid caregiverId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        int status = 4; // CaregiverStatus.Active — the only status discovery reads

        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO caregivers.caregivers (
                    id, user_id, type, status, availability, show_profile,
                    average_rating, reviews_count,
                    medical_specialization_id, companion_specialization_id, medical_professional_title_id,
                    medical_years_of_experience, companion_years_of_experience,
                    companion_hourly_price, medical_eight_hour_shift_price)
                VALUES ($id, $userId, 1, $status, 1, 1, 4.5, 10,
                    $nursingSpec, NULL, NULL,
                    5, NULL,
                    NULL, 400.0);
                """;
            command.Parameters.AddWithValue("$id", caregiverId);
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$status", status);
            command.Parameters.AddWithValue("$nursingSpec", NursingSpecializationId);
            command.ExecuteNonQuery();
        }

        await ExecAsync(
            "INSERT INTO caregivers.caregiver_area_selections (caregiver_id, area_id) VALUES ($caregiver, $area);",
            Now,
            ("$caregiver", caregiverId),
            ("$area", areaId));

        await ExecAsync(
            "INSERT INTO identity.users (id, arabic_full_name, english_full_name, gender, avatar_url) " +
            "VALUES ($id, 'أميرة حسن', 'Amira Hassan', 1, 'private/avatars/amira.jpg');",
            Now,
            ("$id", userId));

        return new Seed(caregiverId, userId);
    }

    /// <summary>A Companion caregiver in governorate B, with no stored avatar.</summary>
    public async Task<Seed> SeedGizaCaregiverAsync()
    {
        GizaCityId = Guid.NewGuid();

        await ExecAsync(
            "INSERT INTO caregivers.cities (id, governorate_id, arabic_name, english_name, is_active, created_on_utc, updated_on_utc) " +
            "VALUES ($id, $governorate, 'الدقي', 'Dokki', 1, $now, $now);",
            Now,
            ("$id", GizaCityId),
            ("$governorate", GovernorateB));

        Guid areaId = Guid.NewGuid();
        await ExecAsync(
            "INSERT INTO caregivers.areas (id, city_id, arabic_name, english_name, is_active, created_on_utc, updated_on_utc) " +
            "VALUES ($id, $city, 'شارع جامعة الدول', 'Gameat El Dowal', 1, $now, $now);",
            Now,
            ("$id", areaId),
            ("$city", GizaCityId));

        Guid caregiverId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        int status = 4; // CaregiverStatus.Active — the only status discovery reads

        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText =
                """
                INSERT INTO caregivers.caregivers (
                    id, user_id, type, status, availability, show_profile,
                    average_rating, reviews_count,
                    medical_specialization_id, companion_specialization_id, medical_professional_title_id,
                    medical_years_of_experience, companion_years_of_experience,
                    companion_hourly_price, medical_eight_hour_shift_price)
                VALUES ($id, $userId, 2, $status, 2, 1, 4.8, 25,
                    NULL, NULL, NULL,
                    NULL, 3,
                    90.0, NULL);
                """;
            command.Parameters.AddWithValue("$id", caregiverId);
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$status", status);
            command.ExecuteNonQuery();
        }

        await ExecAsync(
            "INSERT INTO caregivers.caregiver_area_selections (caregiver_id, area_id) VALUES ($caregiver, $area);",
            Now,
            ("$caregiver", caregiverId),
            ("$area", areaId));

        await ExecAsync(
            "INSERT INTO identity.users (id, arabic_full_name, english_full_name, gender, avatar_url) " +
            "VALUES ($id, 'سامي فؤاد', 'Sami Fouad', 2, NULL);",
            Now,
            ("$id", userId));

        GizaCaregiverId = caregiverId;
        return new Seed(caregiverId, userId);
    }

    public Task<Result<PagedResult<CaregiverSearchCardResponse>>> HandleAsync(SearchCaregiversQuery query) =>
        new SearchCaregiversQueryHandler(Db).Handle(query, CancellationToken.None);

    /// <summary>
    /// Runs the statement through the context connection; the timestamp is
    /// bound first, named parameters follow in declaration order.
    /// </summary>
    private Task ExecAsync(string sql, DateTime now, params (string Name, object Value)[] parameters) =>
        Db.Database.ExecuteSqlRawAsync(
            sql,
            parameters
                .Select(parameter => new SqliteParameter(parameter.Name, parameter.Value))
                .Prepend(new SqliteParameter("$now", now))
                .Cast<object>()
                .ToArray());

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
