using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.Sessions;

namespace Sanad.UnitTests.API;

public sealed class AuthControllerPasswordAndSessionTests
{
    [Fact]
    public async Task RequestPasswordReset_ShouldReturnNoContent()
    {
        var controller = CreateController(Result.Success());

        IActionResult result = await controller.RequestPasswordReset(
            new RequestPasswordResetRequest("user@example.com"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task ResetPassword_ShouldReturnNoContent()
    {
        var controller = CreateController(Result.Success());

        IActionResult result = await controller.ResetPassword(
            new ResetPasswordRequest(
                "user@example.com",
                "123456",
                "NewPassword123"),
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task ChangePassword_ShouldReturnUnauthorized_WhenSubjectIsMissing()
    {
        var controller = CreateController(Result.Success());

        IActionResult result = await controller.ChangePassword(
            new ChangePasswordRequest(
                "CurrentPassword123",
                "NewPassword123"),
            CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task LogoutCurrentSession_ShouldReturnBadRequest_WhenHeaderIsMissing()
    {
        UserId userId = UserId.New();
        var controller = CreateController(Result.Success(), userId);

        IActionResult result = await controller.LogoutCurrentSession(
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(badRequest.Value);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Api.Auth.InvalidDeviceSessionHeader", problem.Extensions["code"]);
    }

    [Fact]
    public async Task LogoutCurrentSession_ShouldDispatchAuthenticatedUserAndHeaderSession()
    {
        UserId userId = UserId.New();
        DeviceSessionId sessionId = DeviceSessionId.New();
        var sender = new CapturingSender(Result.Success());
        var controller = CreateController(sender, userId);

        controller.ControllerContext.HttpContext.Request.Headers[
            "X-Device-Session-Id"] = sessionId.Value.ToString();

        IActionResult result = await controller.LogoutCurrentSession(
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        LogoutCurrentSessionCommand command = Assert.IsType<LogoutCurrentSessionCommand>(sender.LastRequest);

        Assert.Equal(userId, command.CurrentUserId);
        Assert.Equal(sessionId, command.DeviceSessionId);
    }

    [Fact]
    public async Task GetActiveSessions_ShouldReturnOk()
    {
        UserId userId = UserId.New();
        ActiveSessionsResponse response = new([]);
        var controller = CreateController(Result<ActiveSessionsResponse>.Success(response), userId);

        IActionResult result = await controller.GetActiveSessions(
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(response, ok.Value);
    }

    [Fact]
    public async Task RevokeSession_ShouldDispatchRouteAndAuthenticatedUser()
    {
        UserId userId = UserId.New();
        DeviceSessionId sessionId = DeviceSessionId.New();
        var sender = new CapturingSender(Result.Success());
        var controller = CreateController(sender, userId);

        IActionResult result = await controller.RevokeSession(
            sessionId.Value,
            CancellationToken.None);

        Assert.IsType<NoContentResult>(result);

        RevokeSessionCommand command = Assert.IsType<RevokeSessionCommand>(sender.LastRequest);

        Assert.Equal(userId, command.CurrentUserId);
        Assert.Equal(sessionId, command.DeviceSessionId);
    }

    private static AuthController CreateController(object response, UserId? userId = null)
    {
        return CreateController(new CapturingSender(response), userId);
    }

    private static AuthController CreateController(CapturingSender sender, UserId? userId = null)
    {
        var controller = new AuthController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        if (userId.HasValue)
        {
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                [
                    new Claim(JwtRegisteredClaimNames.Sub, userId.Value.Value.ToString())
                ], "test"));
        }

        return controller;
    }

    private sealed class CapturingSender : ISender
    {
        private readonly object _response;

        public CapturingSender(object response)
        {
            _response = response;
        }

        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)_response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
