using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.UnitTests.Caregivers;

namespace Sanad.UnitTests.API;

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
