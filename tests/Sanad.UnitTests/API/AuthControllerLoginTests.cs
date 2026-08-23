using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.Login;
using Sanad.Modules.Identity.Application.Authentication.Refresh;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.UnitTests.API;

public sealed class AuthControllerLoginTests
{
    [Fact]
    public async Task Login_ShouldReturnOk_WithLoginResponse()
    {
        LoginResponse response = CreateLoginResponse();
        var controller = CreateController(
            Result<LoginResponse>.Success(response));

        IActionResult result = await controller.Login(
            new LoginRequest(
                "user@example.com",
                "SecurePassword123",
                "Ahmed's iPhone",
                DevicePlatform.iOS,
                "1.0.0"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);

        Assert.Equal(response, ok.Value);
    }

    [Fact]
    public async Task Login_ShouldReturnMappedProblemDetails_WhenApplicationFails()
    {
        var controller = CreateController(
            Result<LoginResponse>.Failure(
                new Error(
                    "Identity.Login.InvalidCredentials",
                    "Internal message")));

        IActionResult result = await controller.Login(
            new LoginRequest(
                "user@example.com",
                "wrong-password",
                "Ahmed's iPhone",
                DevicePlatform.iOS,
                "1.0.0"),
            CancellationToken.None);

        var unauthorized = Assert.IsType<ObjectResult>(result);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            unauthorized.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(
            unauthorized.Value);

        Assert.Equal(
            "Identity.Login.InvalidCredentials",
            problemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task Refresh_ShouldReturnOk_WithRefreshTokenResponse()
    {
        RefreshTokenResponse response = new(
            DeviceSessionId.New(),
            "new-access-token",
            FixedUtcNow.AddMinutes(15),
            "new-refresh-token",
            FixedUtcNow.AddDays(30));

        var controller = CreateController(
            Result<RefreshTokenResponse>.Success(response));

        IActionResult result = await controller.Refresh(
            new RefreshTokenRequest(
                response.DeviceSessionId.Value,
                "refresh-token"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);

        Assert.Equal(response, ok.Value);
    }

    [Fact]
    public async Task RequestElderlyOtp_ShouldReturnNoContent_ForGenericSuccess()
    {
        var controller = CreateController(
            Result.Success());

        IActionResult result = await controller.RequestElderlyOtp(
            new RequestElderlyLoginOtpRequest(
                "+201001234567"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task VerifyElderlyOtp_ShouldReturnOk_WithLoginResponse()
    {
        LoginResponse response = CreateLoginResponse();
        var controller = CreateController(
            Result<LoginResponse>.Success(response));

        IActionResult result = await controller.VerifyElderlyOtp(
            new VerifyElderlyLoginOtpRequest(
                "+201001234567",
                "123456",
                "Elderly Phone",
                DevicePlatform.Android,
                "1.0.0"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);

        Assert.Equal(response, ok.Value);
    }

    private static AuthController CreateController(
        object response)
    {
        var controller = new AuthController(
            new FakeSender(response));

        controller.ControllerContext =
            new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };

        return controller;
    }

    private static LoginResponse CreateLoginResponse()
    {
        return new LoginResponse(
            UserId.New(),
            AuthAccessType.Normal,
            "access-token",
            FixedUtcNow.AddMinutes(15),
            "refresh-token",
            FixedUtcNow.AddDays(30),
            DeviceSessionId.New(),
            EmailVerified: true,
            PhoneVerified: true);
    }

    private sealed class FakeSender : ISender
    {
        private readonly object _response;

        public FakeSender(object response)
        {
            _response = response;
        }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                (TResponse)_response);
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

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private static readonly DateTime FixedUtcNow = new(
        2026,
        8,
        23,
        10,
        0,
        0,
        DateTimeKind.Utc);
}
