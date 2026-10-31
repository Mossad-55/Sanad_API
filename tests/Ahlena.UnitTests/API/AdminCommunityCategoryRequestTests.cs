using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ahlena.API.Controllers;
using Ahlena.API.Controllers.Requests;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Community.Application.Categories;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// PORTAL-03 community category update contract: the route categoryId is
/// authoritative through a dedicated body DTO without Id. Legacy bodies
/// carrying a conflicting Id stay ignored, as before.
/// </summary>
public sealed class AdminCommunityCategoryRequestTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public AdminCommunityCategoryRequestTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void UpdateBody_HasNoIdProperty()
    {
        Assert.Null(typeof(UpdateCommunityCategoryRequest).GetProperty("Id"));
        Assert.NotNull(typeof(UpdateCommunityCategoryRequest).GetProperty("ArabicName"));
        Assert.NotNull(typeof(UpdateCommunityCategoryRequest).GetProperty("EnglishName"));
        Assert.NotNull(typeof(UpdateCommunityCategoryRequest).GetProperty("IsActive"));
    }

    [Fact]
    public async Task Update_ForwardsRouteIdAndUnchangedFields()
    {
        var sender = new CapturingSender();
        var controller = CreateController(sender);
        Guid routeId = Guid.CreateVersion7();

        IActionResult result = await controller.UpdateCategory(
            routeId,
            new UpdateCommunityCategoryRequest("تصنيف", "Category", true),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        UpdateCategoryCommand command = Assert.IsType<UpdateCategoryCommand>(sender.LastRequest);
        Assert.Equal(routeId, command.Id);
        Assert.Equal("تصنيف", command.ArabicName);
        Assert.Equal("Category", command.EnglishName);
        Assert.True(command.IsActive);
    }

    [Fact]
    public async Task Update_ThroughHost_IgnoresConflictingLegacyBodyId()
    {
        Guid routeId = Guid.CreateVersion7();
        Guid legacyId = Guid.CreateVersion7();
        var sender = new CapturingSender();
        using var factory = CreateFactory(sender);
        using var client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken(AccountType.SuperAdmin));

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/admin/community/categories/{routeId:D}",
            new { id = legacyId, arabicName = "تصنيف", englishName = "Category", isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        UpdateCategoryCommand command = Assert.IsType<UpdateCategoryCommand>(sender.LastRequest);
        Assert.Equal(routeId, command.Id);
        Assert.NotEqual(legacyId, command.Id);
        Assert.False(command.IsActive);
    }

    [Fact]
    public async Task Update_ThroughHost_WorksWithoutBodyId()
    {
        Guid routeId = Guid.CreateVersion7();
        var sender = new CapturingSender();
        using var factory = CreateFactory(sender);
        using var client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken(AccountType.SuperAdmin));

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/admin/community/categories/{routeId:D}",
            new { arabicName = "تصنيف", englishName = "Category", isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(routeId, Assert.IsType<UpdateCategoryCommand>(sender.LastRequest).Id);
    }

    [Fact]
    public async Task Update_ThroughHost_PreservesUnauthorizedAndForbidden()
    {
        var sender = new CapturingSender();
        using var anonymousFactory = CreateFactory(sender);
        using var anonymous = CreatePortalClient(anonymousFactory);
        using HttpResponseMessage unauthorized = await anonymous.PutAsJsonAsync(
            $"/api/v1/admin/community/categories/{Guid.CreateVersion7():D}",
            new { arabicName = "تصنيف", englishName = "Category", isActive = true });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Null(sender.LastRequest);

        using var familyFactory = CreateFactory(new CapturingSender());
        using var family = CreatePortalClient(familyFactory);
        family.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken(AccountType.Family));
        using HttpResponseMessage forbidden = await family.PutAsJsonAsync(
            $"/api/v1/admin/community/categories/{Guid.CreateVersion7():D}",
            new { arabicName = "تصنيف", englishName = "Category", isActive = true });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Update_ResponseMatchesGeneratedSchema()
    {
        using HttpClient documentClient = _host.CreatePortalClient();
        using JsonDocument document = JsonDocument.Parse(
            await (await documentClient.GetAsync("/openapi/v1.json")).Content.ReadAsStringAsync());
        JsonElement schema = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/v1/admin/community/categories/{categoryId}")
            .GetProperty("put")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        Assert.Equal(
            "#/components/schemas/AdminCategoryResponse",
            schema.GetProperty("$ref").GetString());
    }

    private static AdminCommunityCategoriesController CreateController(CapturingSender sender)
    {
        var controller = new AdminCommunityCategoriesController(sender);
        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(
                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                    Guid.CreateVersion7().ToString("D"))],
                "Test"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateFactory(
        CapturingSender sender) =>
        _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(sender);
            }));

    private static HttpClient CreatePortalClient(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private string CreateAccessToken(AccountType accountType)
    {
        User user = User.Create(
            FullName.Create("مدير"),
            FullName.Create("Admin"),
            Email.Create("portal.admin@example.test"),
            PhoneNumber.Create("+201000003001"));
        user.AddAccount(accountType);
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        return tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;
    }

    private sealed class CapturingSender : ISender
    {
        private static readonly AdminCategoryResponse Response = new(
            Guid.CreateVersion7(), "تصنيف", "Category", true, DateTime.UtcNow, null);

        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            Assert.IsType<UpdateCategoryCommand>(request);
            return Task.FromResult((TResponse)(object)Result<AdminCategoryResponse>.Success(Response));
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}

