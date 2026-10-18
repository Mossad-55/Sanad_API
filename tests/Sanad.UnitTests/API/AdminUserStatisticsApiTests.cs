using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Users.Admin;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

/// <summary>
/// Focused E01 HTTP evidence over the isolated
/// <see cref="PortalApiMetadataHost"/>: SuperAdmin authorization and the
/// aggregate-only statistics JSON contract. Synthetic dispatch data only; no
/// database, provider or migration.
/// </summary>
public sealed class AdminUserStatisticsApiTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public AdminUserStatisticsApiTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public async Task Statistics_SuperAdmin_ReturnsAggregateOnlyContract()
    {
        using var factory = OverrideSender(_host, new CannedStatisticsSender());
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CareHomeOwnerPortalFixture.CreateToken(
                _host,
                CareHomeOwnerPortalFixture.CreateUser(AccountType.SuperAdmin, 71)));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/users/statistics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = body.RootElement;
        Assert.Equal(7, root.GetProperty("totalUsers").GetInt32());

        JsonElement statuses = root.GetProperty("usersByStatus");
        Assert.Equal(JsonValueKind.Array, statuses.ValueKind);
        Assert.Equal(4, statuses.GetArrayLength());
        Assert.Equal((int)UserStatus.PendingVerification, statuses[0].GetProperty("status").GetInt32());
        Assert.Equal(3, statuses[0].GetProperty("count").GetInt32());
        Assert.Equal((int)UserStatus.Active, statuses[1].GetProperty("status").GetInt32());
        Assert.Equal(4, statuses[1].GetProperty("count").GetInt32());
        Assert.Equal((int)UserStatus.Suspended, statuses[2].GetProperty("status").GetInt32());
        Assert.Equal(0, statuses[2].GetProperty("count").GetInt32());
        Assert.Equal((int)UserStatus.Blocked, statuses[3].GetProperty("status").GetInt32());
        Assert.Equal(0, statuses[3].GetProperty("count").GetInt32());

        JsonElement accountTypes = root.GetProperty("usersByAccountType");
        Assert.Equal(JsonValueKind.Array, accountTypes.ValueKind);
        Assert.Equal(9, accountTypes.GetArrayLength());
        Assert.Equal((int)AccountType.Family, accountTypes[0].GetProperty("accountType").GetInt32());
        Assert.Equal(3, accountTypes[0].GetProperty("distinctUserCount").GetInt32());
        Assert.Equal((int)AccountType.CompanionCaregiver, accountTypes[2].GetProperty("accountType").GetInt32());
        Assert.Equal(1, accountTypes[2].GetProperty("distinctUserCount").GetInt32());
        Assert.Equal(0, accountTypes[8].GetProperty("distinctUserCount").GetInt32());

        JsonElement months = root.GetProperty("newRegistrationsByMonth");
        Assert.Equal(12, months.GetArrayLength());
        Assert.Equal("2025-11", months[0].GetProperty("month").GetString());
        Assert.Equal("2026-10", months[11].GetProperty("month").GetString());
        Assert.Equal(2, months[11].GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Statistics_Anonymous_IsUnauthorized()
    {
        using var factory = OverrideSender(_host, new CannedStatisticsSender());
        using HttpClient client = CreateClient(factory);

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/users/statistics");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> OverrideSender(
        PortalApiMetadataHost host,
        ISender sender) =>
        host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISender>();
            services.AddSingleton(sender);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private sealed class CannedStatisticsSender : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                AdminUserStatisticsQuery => Result<AdminUserStatisticsResponse>.Success(
                    new(
                        7,
                        new[]
                        {
                            new UserStatusCount(UserStatus.PendingVerification, 3),
                            new UserStatusCount(UserStatus.Active, 4),
                            new UserStatusCount(UserStatus.Suspended, 0),
                            new UserStatusCount(UserStatus.Blocked, 0),
                        },
                        new[]
                        {
                            new AccountTypeCount(AccountType.Family, 3),
                            new AccountTypeCount(AccountType.MedicalCaregiver, 1),
                            new AccountTypeCount(AccountType.CompanionCaregiver, 1),
                            new AccountTypeCount(AccountType.Elderly, 1),
                            new AccountTypeCount(AccountType.SuperAdmin, 1),
                            new AccountTypeCount(AccountType.ContentAdmin, 0),
                            new AccountTypeCount(AccountType.SupportAdmin, 1),
                            new AccountTypeCount(AccountType.CareHomeOwner, 1),
                            new AccountTypeCount(AccountType.FinanceAdmin, 0),
                        },
                        Enumerable.Range(0, 12).Select(i =>
                            new UserRegistrationMonthBucket(
                                new DateTime(2025, 11, 1).AddMonths(i).ToString("yyyy-MM"),
                                i == 11 ? 2 : 0)).ToArray())),
                _ => throw new NotSupportedException(
                    $"CannedSender has no canned result for {request.GetType().FullName}.")
            };
            return Task.FromResult((TResponse)result);
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
