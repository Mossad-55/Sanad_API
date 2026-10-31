using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Caregivers.Domain.Caregivers;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.Modules.Identity.Infrastructure.Persistence;
using Ahlena.UnitTests.Caregivers;

namespace Ahlena.UnitTests.API;

/// <summary>
/// FUR-09: HTTP-level evidence for GET /api/v1/admin/caregivers through the
/// real controller, CaregiverReviewAdmin policy, MediatR handler and the
/// production caregiver list/count SQL. The only replaced dependency is the
/// Caregivers context: the existing CaregiverIntegrityFixture SQLite attached
/// schemas (caregivers + identity) on a kept-open :memory: connection. Every
/// other context stays on the isolated InMemory host registrations; seeders,
/// migrations and background jobs remain disabled. All data is synthetic.
/// This proves local API behavior only; it is not PostgreSQL or Production
/// evidence and does not diagnose the six reported Production rows.
/// </summary>
public sealed class CaregiverAdminListApiTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public CaregiverAdminListApiTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public async Task AdminCaregivers_EligibleSuperAdmin_ReturnsAllSyntheticRows_WithEnrichmentAndNulls()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            // Precondition readback on the fixture connection before any HTTP
            // request: the exact rows the assertions below will consume.
            ReadBackScenarioRows(seeded.Connection);

            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            using var factory = OverrideCaregivers(_host, db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(_host, CreateUser(AccountType.SuperAdmin, 1)));

            using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/caregivers");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
            Assert.Equal(10, body.RootElement.GetProperty("pageSize").GetInt32());
            Assert.Equal(5, body.RootElement.GetProperty("totalCount").GetInt32());
            JsonElement items = body.RootElement.GetProperty("items");
            Assert.Equal(5, items.GetArrayLength());

            // Matched caregiver: stored Identity name/phone enrichment.
            JsonElement matched = FindItem(items, seeded.Ids.MatchedMedicalCaregiver.ToString("D"));
            Assert.Equal(CaregiverIntegrityFixture.MatchedMedicalUserId.ToString("D"),
                matched.GetProperty("userId").GetString());
            Assert.Equal("اسم مطابق", matched.GetProperty("arabicFullName").GetString());
            Assert.Equal("Matched Medical", matched.GetProperty("englishFullName").GetString());
            Assert.Equal("+10000000001", matched.GetProperty("phoneNumber").GetString());
            Assert.Equal((int)CaregiverType.Medical, matched.GetProperty("type").GetInt32());

            // The second matched caregiver (Companion) also enriches.
            JsonElement matchedCompanion = FindItem(items, seeded.Ids.MatchedCompanionCaregiver.ToString("D"));
            Assert.Equal("Matched Companion", matchedCompanion.GetProperty("englishFullName").GetString());
            Assert.Equal("+10000000005", matchedCompanion.GetProperty("phoneNumber").GetString());

            // Orphan caregivers: present with null enrichment, still counted.
            JsonElement orphan = FindItem(items, seeded.Ids.OrphanCompanionCaregiver.ToString("D"));
            Assert.Equal(CaregiverIntegrityFixture.OrphanCompanionUserId.ToString("D"),
                orphan.GetProperty("userId").GetString());
            Assert.Equal(JsonValueKind.Null, orphan.GetProperty("arabicFullName").ValueKind);
            Assert.Equal(JsonValueKind.Null, orphan.GetProperty("englishFullName").ValueKind);
            Assert.Equal(JsonValueKind.Null, orphan.GetProperty("phoneNumber").ValueKind);

            JsonElement secondOrphan = FindItem(items, seeded.Ids.SecondOrphanCaregiver.ToString("D"));
            Assert.Equal(JsonValueKind.Null, secondOrphan.GetProperty("englishFullName").ValueKind);
            Assert.Equal(JsonValueKind.Null, secondOrphan.GetProperty("phoneNumber").ValueKind);

            // Wrong account type: the row stays visible with its stored
            // enrichment; the list neither filters nor rewrites it.
            JsonElement wrongRole = FindItem(items, seeded.Ids.WrongRoleCaregiver.ToString("D"));
            Assert.Equal("Wrong Role", wrongRole.GetProperty("englishFullName").GetString());
            Assert.Equal("+10000000003", wrongRole.GetProperty("phoneNumber").GetString());
            Assert.Equal((int)CaregiverType.Companion, wrongRole.GetProperty("type").GetInt32());
        }
    }

    [Fact]
    public async Task AdminCaregivers_SupportAdmin_IsEligible()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            using var factory = OverrideCaregivers(_host, db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(_host, CreateUser(AccountType.SupportAdmin, 2)));

            using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/caregivers");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task AdminCaregivers_Anonymous_IsUnauthorized()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            using var factory = OverrideCaregivers(_host, db);
            using HttpClient client = CreateClient(factory);

            using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/caregivers");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task AdminCaregivers_NonEligibleFamily_IsForbidden()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            using var factory = OverrideCaregivers(_host, db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(_host, CreateUser(AccountType.Family, 3)));

            using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/caregivers");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task AdminCaregivers_StatusFilter_AppliesToListAndCount()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            using var factory = OverrideCaregivers(_host, db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(_host, CreateUser(AccountType.SuperAdmin, 4)));

            using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/caregivers?status=4");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(2, body.RootElement.GetProperty("totalCount").GetInt32());
            JsonElement items = body.RootElement.GetProperty("items");
            Assert.Equal(2, items.GetArrayLength());
            foreach (JsonElement item in items.EnumerateArray())
            {
                Assert.Equal((int)CaregiverStatus.Active, item.GetProperty("status").GetInt32());
            }
        }
    }

    [Fact]
    public async Task AdminCaregivers_TypeFilter_KeepsUnmatchedRows()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            using var factory = OverrideCaregivers(_host, db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(_host, CreateUser(AccountType.SuperAdmin, 5)));

            using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/caregivers?type=2");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(3, body.RootElement.GetProperty("totalCount").GetInt32());
            JsonElement items = body.RootElement.GetProperty("items");
            Assert.Equal(3, items.GetArrayLength());
            foreach (JsonElement item in items.EnumerateArray())
            {
                Assert.Equal((int)CaregiverType.Companion, item.GetProperty("type").GetInt32());
            }

            // The orphan Companion caregiver stays in the filtered list with
            // null enrichment; the type filter never hides unmatched rows.
            JsonElement orphan = FindItem(items, seeded.Ids.OrphanCompanionCaregiver.ToString("D"));
            Assert.Equal(JsonValueKind.Null, orphan.GetProperty("englishFullName").ValueKind);
        }
    }

    [Fact]
    public async Task AdminCaregivers_Paging_ReturnsDeterministicWindows_WithFullTotal()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeeded();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            using var factory = OverrideCaregivers(_host, db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(_host, CreateUser(AccountType.SuperAdmin, 6)));

            using HttpResponseMessage page1 = await client.GetAsync("/api/v1/admin/caregivers?page=1&pageSize=2");
            Assert.Equal(HttpStatusCode.OK, page1.StatusCode);
            using JsonDocument body1 = JsonDocument.Parse(await page1.Content.ReadAsStringAsync());
            Assert.Equal(5, body1.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(
                [seeded.Ids.MatchedMedicalCaregiver.ToString("D"), seeded.Ids.OrphanCompanionCaregiver.ToString("D")],
                ItemIds(body1));

            using HttpResponseMessage page2 = await client.GetAsync("/api/v1/admin/caregivers?page=2&pageSize=2");
            Assert.Equal(HttpStatusCode.OK, page2.StatusCode);
            using JsonDocument body2 = JsonDocument.Parse(await page2.Content.ReadAsStringAsync());
            Assert.Equal(5, body2.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(
                [seeded.Ids.WrongRoleCaregiver.ToString("D"), seeded.Ids.SecondOrphanCaregiver.ToString("D")],
                ItemIds(body2));

            using HttpResponseMessage page3 = await client.GetAsync("/api/v1/admin/caregivers?page=3&pageSize=2");
            Assert.Equal(HttpStatusCode.OK, page3.StatusCode);
            using JsonDocument body3 = JsonDocument.Parse(await page3.Content.ReadAsStringAsync());
            Assert.Equal(5, body3.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal([seeded.Ids.MatchedCompanionCaregiver.ToString("D")], ItemIds(body3));

            // Empty window still reports the full total (no silent count shift).
            using HttpResponseMessage empty = await client.GetAsync("/api/v1/admin/caregivers?page=10&pageSize=10");
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            using JsonDocument emptyBody = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
            Assert.Equal(5, emptyBody.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Empty(ItemIds(emptyBody));
        }
    }

    // ---------------- W4: caregiver list vs Identity detail ------------------
    //
    // Facts for the three causes the Identity detail path can distinguish:
    // AUTHORIZATION (SupportAdmin is eligible on the caregiver list but
    // SuperAdminOnly gates Identity detail -> 403), MISSING IDENTITY
    // (SuperAdmin -> 404 Identity.User.NotFound) and ANONYMIZATION
    // (SuperAdmin -> 200 with the placeholder name pair). The list projects
    // `identityPresent`, which means ONLY "an identity.users row was joined on
    // the configured query target during this read": it alone distinguishes
    // neither anonymization nor deactivation (both join, so both are true),
    // and a false value is not authoritative cross-target identity absence
    // (wrong-id joins, stale copies and split targets remain outside its
    // meaning — PORTAL-04 runbook). The 404 is likewise target-local: it says
    // only that no identity.users row was found on the queried target.

    private const string IdentityInMemoryProvider = "Microsoft.EntityFrameworkCore.InMemory";

    [Fact]
    public async Task AdminIdentityDetail_SupportAdmin_IsForbidden()
    {
        // Authorization comes first: the same SupportAdmin that is eligible for
        // the caregiver list cannot open the Identity detail at all. The denial
        // is policy-shaped (403), never a data judgement about the target.
        using HttpClient client = _host.CreatePortalClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(_host, CreateUser(AccountType.SupportAdmin, 11)));

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/admin/users/{CaregiverIntegrityFixture.OrphanCompanionUserId.ToString("D")}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminIdentityDetail_SuperAdmin_MissingIdentityUser_IsNotFoundWithCode()
    {
        await using IdentityDbContext identity = CreateIsolatedIdentityDbContext();

        // Execution guard: the identity context under test is an isolated
        // InMemory store — no PostgreSQL connection is possible.
        Assert.Equal(IdentityInMemoryProvider, identity.Database.ProviderName);

        // No user is seeded: the caregiver list's user_id has no matching
        // identity row on the queried target, which is today's 404 shape.
        using var factory = OverrideIdentity(_host, identity);
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(_host, CreateUser(AccountType.SuperAdmin, 12)));

        using HttpResponseMessage response = await client.GetAsync(
            $"/api/v1/admin/users/{CaregiverIntegrityFixture.OrphanCompanionUserId.ToString("D")}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Identity.User.NotFound", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AdminIdentityDetail_SuperAdmin_AnonymizedUser_ShowsPlaceholderNames()
    {
        await using IdentityDbContext identity = CreateIsolatedIdentityDbContext();
        Assert.Equal(IdentityInMemoryProvider, identity.Database.ProviderName);

        // Today's anonymization write shape (User.AnonymizeAndDeactivate):
        // the row stays, the names become the placeholder pair, email is
        // cleared, the phone becomes a synthetic "+200..." number and the
        // status becomes Blocked.
        User anonymized = User.Create(
            FullName.Create("اسم حقيقي"),
            FullName.Create("Real Name"),
            Email.Create("anonymized-user@example.test"),
            PhoneNumber.Create("+201099999901"));
        anonymized.AddAccount(AccountType.Family);
        anonymized.AnonymizeAndDeactivate(
            "Portal test anonymization",
            new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        identity.Users.Add(anonymized);
        await identity.SaveChangesAsync();

        // Read back the persisted id: User.Create generates it, so the detail
        // request targets the actual stored row.
        Guid anonymizedUserId = anonymized.Id.Value;

        using var factory = OverrideIdentity(_host, identity);
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CreateToken(_host, CreateUser(AccountType.SuperAdmin, 13)));

        using HttpResponseMessage response =
            await client.GetAsync($"/api/v1/admin/users/{anonymizedUserId.ToString("D")}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;
        Assert.Equal(anonymizedUserId.ToString("D"), root.GetProperty("id").GetString());
        Assert.Equal("محذوف", root.GetProperty("arabicName").GetString());
        Assert.Equal("Deleted", root.GetProperty("englishName").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("email").ValueKind);
        Assert.StartsWith("+200", root.GetProperty("phone").GetString(), StringComparison.Ordinal);
        Assert.Equal(4, root.GetProperty("status").GetInt32()); // UserStatus.Blocked
        Assert.Equal(1, root.GetProperty("accountTypes").GetArrayLength());
        Assert.Equal(1, root.GetProperty("accountTypes")[0].GetInt32()); // AccountType.Family
        Assert.True(root.GetProperty("isVerified").GetBoolean());

        // Anonymization is deactivation on the Identity side, plainly visible
        // in the detail — not a missing row and not a 404.
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("phone").ValueKind);
    }

    [Fact]
    public async Task AdminCaregivers_DeactivatedFilter_ListsRowWithIntactNames()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeededWithIntegrityClasses();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            using var factory = OverrideCaregivers(_host, db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(_host, CreateUser(AccountType.SuperAdmin, 14)));

            using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/caregivers?status=7");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
            JsonElement items = body.RootElement.GetProperty("items");
            JsonElement deactivated = Assert.Single(items.EnumerateArray());
            Assert.Equal(CaregiverIntegrityFixture.DeactivatedCaregiverId.ToString("D"),
                deactivated.GetProperty("caregiverId").GetString());
            Assert.Equal(7, deactivated.GetProperty("status").GetInt32());
            Assert.Equal("Deactivated User", deactivated.GetProperty("englishFullName").GetString());
            Assert.Equal("+10000000007", deactivated.GetProperty("phoneNumber").GetString());
            // The deactivated caregiver's identity row joined, so the wire
            // flag is true — identityPresent does not mark deactivation.
            Assert.True(deactivated.GetProperty("identityPresent").GetBoolean());
        }
    }

    [Fact]
    public async Task AdminCaregivers_AnonymizedShapeRow_ShowsPlaceholderNames_NotNulls()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeededWithIntegrityClasses();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            using var factory = OverrideCaregivers(_host, db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(_host, CreateUser(AccountType.SuperAdmin, 15)));

            using HttpResponseMessage response = await client.GetAsync(
                $"/api/v1/admin/caregivers?type=1");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement items = body.RootElement.GetProperty("items");

            JsonElement anonymized = FindItem(items, CaregiverIntegrityFixture.AnonymizedCaregiverId.ToString("D"));
            Assert.Equal("محذوف", anonymized.GetProperty("arabicFullName").GetString());
            Assert.Equal("Deleted", anonymized.GetProperty("englishFullName").GetString());
            Assert.Equal(CaregiverIntegrityFixture.AnonymizedPhone, anonymized.GetProperty("phoneNumber").GetString());
            // The anonymized identity row exists and joined: identityPresent
            // is true; the flag alone does not reveal anonymization.
            Assert.True(anonymized.GetProperty("identityPresent").GetBoolean());

            // The same filtered list still contains the missing-identity row
            // in its distinct null shape — and its identityPresent is false,
            // the only polarity the flag alone can separate.
            JsonElement secondOrphan = FindItem(items, seeded.BaseIds.SecondOrphanCaregiver.ToString("D"));
            Assert.Equal(JsonValueKind.Null, secondOrphan.GetProperty("arabicFullName").ValueKind);
            Assert.False(secondOrphan.GetProperty("identityPresent").GetBoolean());
        }
    }

    [Fact]
    public async Task AdminCaregivers_IdentityPresent_WirePolarity_IsJoinedRowOnly()
    {
        var seeded = CaregiverIntegrityFixture.CreateSeededWithIntegrityClasses();
        using (seeded.Connection)
        {
            using CaregiversDbContext db = CaregiverIntegrityFixture.CreateDbContext(seeded.Connection);
            using var factory = OverrideCaregivers(_host, db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(_host, CreateUser(AccountType.SuperAdmin, 16)));

            using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/caregivers?pageSize=100");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement items = body.RootElement.GetProperty("items");
            Assert.Equal(7, items.GetArrayLength());

            // Wire contract pinned: exactly the two orphan rows report
            // identityPresent false; every joined row — matched, anonymized-
            // shape and deactivated — reports true.
            var falseIds = new List<string>();
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (!item.GetProperty("identityPresent").GetBoolean())
                {
                    falseIds.Add(item.GetProperty("caregiverId").GetString()!);
                }
            }

            Assert.Equal(2, falseIds.Count);
            Assert.Contains(seeded.BaseIds.OrphanCompanionCaregiver.ToString("D"), falseIds);
            Assert.Contains(seeded.BaseIds.SecondOrphanCaregiver.ToString("D"), falseIds);

            JsonElement anonymized = FindItem(items, CaregiverIntegrityFixture.AnonymizedCaregiverId.ToString("D"));
            Assert.True(anonymized.GetProperty("identityPresent").GetBoolean());
            JsonElement deactivated = FindItem(items, CaregiverIntegrityFixture.DeactivatedCaregiverId.ToString("D"));
            Assert.True(deactivated.GetProperty("identityPresent").GetBoolean());
            JsonElement matched = FindItem(items, seeded.BaseIds.MatchedMedicalCaregiver.ToString("D"));
            Assert.True(matched.GetProperty("identityPresent").GetBoolean());
        }
    }

    // Replaces ONLY the Caregivers context registrations with the fixture's
    // SQLite attached-schema instance; every other host registration (InMemory
    // contexts, disabled seeders/migrations/jobs, test JWT options) is kept.
    private static WebApplicationFactory<Program> OverrideCaregivers(
        PortalApiMetadataHost host, CaregiversDbContext db) =>
        host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<CaregiversDbContext>();
            services.RemoveAll<ICaregiversDbContext>();
            services.AddSingleton(db);
            services.AddSingleton<ICaregiversDbContext>(db);
        }));

    // Same isolated replacement pattern for the Identity context: a per-test
    // InMemory store (unique database name) registered for both the concrete
    // context and the application seam the Identity handlers consume. No
    // production connection string is ever opened.
    private static WebApplicationFactory<Program> OverrideIdentity(
        PortalApiMetadataHost host, IdentityDbContext db) =>
        host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IdentityDbContext>();
            services.RemoveAll<IIdentityDbContext>();
            services.AddSingleton(db);
            services.AddSingleton<IIdentityDbContext>(db);
        }));

    private static IdentityDbContext CreateIsolatedIdentityDbContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static string CreateToken(PortalApiMetadataHost host, User user)
    {
        IAuthTokenService tokens = host.Services.GetRequiredService<IAuthTokenService>();
        return tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;
    }

    private static User CreateUser(AccountType accountType, int index)
    {
        User user = User.Create(
            FullName.Create($"مستخدم {index}"),
            FullName.Create($"Portal User {index}"),
            Email.Create($"portaluser{index}@example.test"),
            PhoneNumber.Create($"+20100000{index:D4}"));
        user.AddAccount(accountType);
        return user;
    }

    private static JsonElement FindItem(JsonElement items, string caregiverId)
    {
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.GetProperty("caregiverId").GetString() == caregiverId)
            {
                return item;
            }
        }

        Assert.Fail($"Expected caregiver {caregiverId} was not present in the response.");
        return default;
    }

    private static List<string> ItemIds(JsonDocument body) =>
        [.. body.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("caregiverId").GetString()!)];

    // Precondition readback on the fixture connection before any HTTP request:
    // the exact rows the HTTP assertions will consume.
    private static void ReadBackScenarioRows(SqliteConnection connection)
    {
        const string sql =
            """
            SELECT c.id, u.english_full_name, u.phone_number
            FROM caregivers.caregivers c
            LEFT JOIN identity.users u ON u.id = c.user_id
            WHERE c.id IN ($matched, $orphan, $wrongRole)
            ORDER BY c.id
            """;

        var rows = new List<(string Id, string? Name, string? Phone)>();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$matched", CaregiverIntegrityFixture.MatchedMedicalCaregiverId.ToString("D"));
        command.Parameters.AddWithValue("$orphan", CaregiverIntegrityFixture.OrphanCompanionCaregiverId.ToString("D"));
        command.Parameters.AddWithValue("$wrongRole", CaregiverIntegrityFixture.WrongRoleCaregiverId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        Assert.Equal(3, rows.Count);
        Assert.Equal(
            (CaregiverIntegrityFixture.MatchedMedicalCaregiverId.ToString("D"), "Matched Medical", "+10000000001"),
            rows[0]);
        Assert.Equal(
            (CaregiverIntegrityFixture.OrphanCompanionCaregiverId.ToString("D"), null, null),
            rows[1]);
        Assert.Equal(
            (CaregiverIntegrityFixture.WrongRoleCaregiverId.ToString("D"), "Wrong Role", "+10000000003"),
            rows[2]);
    }
}
