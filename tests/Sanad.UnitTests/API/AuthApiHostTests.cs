using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Sessions;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Users;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Sanad.Modules.Identity.Infrastructure.Security;

namespace Sanad.UnitTests.API;

public sealed class AuthApiHostTests :
    IClassFixture<AuthApiHostTests.SanadApiFactory>
{
    private readonly SanadApiFactory _factory;

    public AuthApiHostTests(
        SanadApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LoginRoute_ShouldBeMapped_AndReturnValidationProblemDetails()
    {
        using HttpClient client =
            CreateClient(
                _factory);

        var request =
            new LoginRequest(
                "not-an-email",
                string.Empty,
                string.Empty,
                DevicePlatform.Unknown,
                string.Empty);

        HttpResponseMessage response =
            await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers
                .ContentType?
                .MediaType);

        using JsonDocument document =
            await JsonDocument.ParseAsync(
                await response.Content
                    .ReadAsStreamAsync());

        Assert.Equal(
            "Api.Validation.Failed",
            document.RootElement
                .GetProperty("code")
                .GetString());

        Assert.True(
            document.RootElement
                .TryGetProperty(
                    "errors",
                    out JsonElement errors));

        Assert.Equal(
            JsonValueKind.Object,
            errors.ValueKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-valid-jwt")]
    public async Task NormalEndpoint_ShouldReturnUnauthorized_WhenTokenIsMissingOrInvalid(
        string? accessToken)
    {
        using HttpClient client =
            CreateClient(
                _factory);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/auth/sessions/logout");

        if (accessToken is not null)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);
        }

        HttpResponseMessage response =
            await client.SendAsync(
                request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task NormalEndpoint_ShouldReturnForbidden_ForRestrictedVerificationToken()
    {
        using HttpClient client =
            CreateClient(
                _factory);

        User user =
            CreateUser();

        string accessToken =
            CreateAccessToken(
                user,
                AuthAccessType
                    .RestrictedVerification);

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                "/api/v1/auth/sessions/logout");

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        HttpResponseMessage response =
            await client.SendAsync(
                request);

        string authenticationChallenge =
            string.Join(
                ", ",
                response.Headers.WwwAuthenticate
                    .Select(value =>
                        value.ToString()));

        string responseBody =
            await response.Content
                .ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    private string CreateAccessToken(
        User user,
        AuthAccessType accessType)
    {
        IAuthTokenService tokenService =
            _factory.Services
                .GetRequiredService<
                    IAuthTokenService>();

        DateTime utcNow =
            DateTime.UtcNow
                .AddSeconds(-5);

        GeneratedAccessToken token =
            accessType switch
            {
                AuthAccessType.Normal =>
                    tokenService
                        .GenerateAccessToken(
                            user,
                            utcNow),

                AuthAccessType
                    .RestrictedVerification =>
                        tokenService
                            .GenerateRestrictedVerificationToken(
                                user,
                                utcNow),

                _ => throw new ArgumentOutOfRangeException(
                    nameof(accessType))
            };

        return token.PlainTextToken;
    }

    private static User CreateUser()
    {
        User user =
            User.Create(
                FullName.Create(
                    "محمد أحمد"),
                FullName.Create(
                    "Mohamed Ahmed"),
                Email.Create(
                    "mohamed@example.com"),
                PhoneNumber.Create(
                    "+201001234567"));

        user.AddAccount(
            AccountType.Family);

        return user;
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress =
                    new Uri(
                        "https://localhost"),

                AllowAutoRedirect =
                    false
            });
    }

    public sealed class SanadApiFactory :
    WebApplicationFactory<Program>
    {
        private const string TestIssuer =
            "Sanad.HostTests";

        private const string TestAudience =
            "Sanad.TestClients";

        private const string TestSigningKey =
            "host-tests-only-signing-key-" +
            "not-for-production-123456789";

        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment(
                "Development");

            builder.ConfigureAppConfiguration(
                (
                    _,
                    configuration) =>
                {
                    configuration
                        .AddInMemoryCollection(
                            new Dictionary<
                                string,
                                string?>
                            {
                                [
                                    "ConnectionStrings:" +
                                    "IdentityDatabase"
                                ] =
                                    "Host=localhost;" +
                                    "Port=1;" +
                                    "Database=sanad_host_tests;" +
                                    "Username=test;" +
                                    "Password=test",

                                [
                                    "Identity:Jwt:Issuer"
                                ] =
                                    TestIssuer,

                                [
                                    "Identity:Jwt:Audience"
                                ] =
                                    TestAudience,

                                [
                                    "Identity:Jwt:SigningKey"
                                ] =
                                    TestSigningKey
                            });
                });

            builder.ConfigureTestServices(
                services =>
                {
                    services.RemoveAll<
                        IOptions<JwtOptions>>();

                    services.AddSingleton<
                        IOptions<JwtOptions>>(
                            Options.Create(
                                new JwtOptions
                                {
                                    Issuer =
                                        TestIssuer,

                                    Audience =
                                        TestAudience,

                                    SigningKey =
                                        TestSigningKey
                                }));

                    services.PostConfigure<
                        JwtBearerOptions>(
                            JwtBearerDefaults
                                .AuthenticationScheme,
                            options =>
                            {
                                var signingKey =
                                    new SymmetricSecurityKey(
                                        Encoding.UTF8
                                            .GetBytes(
                                                TestSigningKey));

                                options.MapInboundClaims =
                                    false;

                                options
                                    .TokenValidationParameters
                                    .ValidateIssuer =
                                        true;

                                options
                                    .TokenValidationParameters
                                    .ValidIssuer =
                                        TestIssuer;

                                options
                                    .TokenValidationParameters
                                    .ValidateAudience =
                                        true;

                                options
                                    .TokenValidationParameters
                                    .ValidAudience =
                                        TestAudience;

                                options
                                    .TokenValidationParameters
                                    .ValidateIssuerSigningKey =
                                        true;

                                options
                                    .TokenValidationParameters
                                    .IssuerSigningKey =
                                        signingKey;

                                options
                                    .TokenValidationParameters
                                    .TryAllIssuerSigningKeys =
                                        true;
                            });
                });
        }
    }

    private sealed class CapturingSender :
        ISender
    {
        public object? LastRequest
        {
            get;
            private set;
        }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest =
                request;

            if (request is
                LogoutCurrentSessionCommand)
            {
                return Task.FromResult(
                    (TResponse)(object)
                    Result.Success());
            }

            throw new NotSupportedException(
                $"Unexpected request type: " +
                $"{request.GetType().Name}.");
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException();
        }

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<TResponse>
            CreateStream<TResponse>(
                IStreamRequest<TResponse> request,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?>
            CreateStream(
                object request,
                CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}