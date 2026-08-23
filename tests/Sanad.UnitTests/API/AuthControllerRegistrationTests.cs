using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Application.Authentication.Verification;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

public sealed class AuthControllerRegistrationTests
{
    [Fact]
    public async Task Register_ShouldReturnCreated_WithApplicationResponse()
    {
        RegisterUserResponse response = new(
            UserId.New(),
            VerificationRequestId.New(),
            VerificationRequestId.New());

        var controller = CreateController(
            Result<RegisterUserResponse>.Success(response));

        IActionResult result = await controller.Register(
            new RegisterRequest(
                "محمد أحمد",
                "Mohamed Ahmed",
                "mohamed@example.com",
                "+201001234567",
                "SecurePassword123",
                AccountType.Family,
                AvatarUrl: null),
            CancellationToken.None);

        var created = Assert.IsType<ObjectResult>(result);

        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Equal(response, created.Value);
    }

    [Fact]
    public async Task Register_ShouldReturnMappedProblemDetails_WhenApplicationFails()
    {
        var controller = CreateController(
            Result<RegisterUserResponse>.Failure(
                new Error(
                    "Identity.Registration.EmailAlreadyInUse",
                    "Internal message")));

        IActionResult result = await controller.Register(
            new RegisterRequest(
                "محمد أحمد",
                "Mohamed Ahmed",
                "mohamed@example.com",
                "+201001234567",
                "SecurePassword123",
                AccountType.Family,
                AvatarUrl: null),
            CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(result);

        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(conflict.Value);

        Assert.Equal(
            "Identity.Registration.EmailAlreadyInUse",
            problemDetails.Extensions["code"]);
    }

    [Fact]
    public async Task VerifyOtp_ShouldReturnOk_WithApplicationResponse()
    {
        VerifyOtpResponse response = new(
            UserId.New(),
            EmailVerified: true,
            PhoneVerified: true,
            NormalAccessAllowed: true,
            AttemptesRemaining: 5);

        var controller = CreateController(
            Result<VerifyOtpResponse>.Success(response));

        IActionResult result = await controller.VerifyOtp(
            new VerifyOtpRequest(
                Guid.CreateVersion7(),
                "123456"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);

        Assert.Equal(response, ok.Value);
    }

    [Fact]
    public async Task ResendOtp_ShouldReturnOk_WithApplicationResponse()
    {
        ResendOtpResponse response = new(
            VerificationRequestId.New(),
            new DateTime(
                2026,
                8,
                23,
                10,
                5,
                0,
                DateTimeKind.Utc));

        var controller = CreateController(
            Result<ResendOtpResponse>.Success(response));

        IActionResult result = await controller.ResendOtp(
            new ResendOtpRequest(
                Guid.CreateVersion7()),
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
}
