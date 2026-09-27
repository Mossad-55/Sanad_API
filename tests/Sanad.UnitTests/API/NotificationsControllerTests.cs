using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.UnitTests.API;

public sealed class NotificationsControllerTests
{
    [Fact]
    public void Controller_ShouldRequireNormalAccess()
    {
        var attribute = Assert.Single(typeof(NotificationsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.NormalAccess, attribute.Policy);
    }

    [Fact]
    public async Task List_ShouldReturnUnauthorizedWithoutAuthenticatedSubject()
    {
        var sender = new CapturingSender();
        var controller = new NotificationsController(sender)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.List(null, cancellationToken: CancellationToken.None);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task List_ShouldForwardAuthenticatedUserAndDefaultPageSize()
    {
        var sender = new CapturingSender();
        var userId = Guid.NewGuid();
        var controller = new NotificationsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([
                        new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())]))
                }
            }
        };

        var result = await controller.List(null, cancellationToken: CancellationToken.None);
        Assert.NotNull(sender.LastRequest);
        var query = sender.LastRequest!;

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("ListNotificationsQuery", query.GetType().Name);
        Assert.Equal(userId, query.GetType().GetProperty("UserId")!.GetValue(query));
        Assert.Equal(20, query.GetType().GetProperty("PageSize")!.GetValue(query));
    }

    [Fact]
    public void NotificationNotFound_ShouldMapTo404()
    {
        var problem = ResultProblemDetailsMapper.Create(
            new Error("Notifications.NotFound", "internal"), new DefaultHttpContext());

        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
    }

    private sealed class CapturingSender : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            var pageType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("Sanad.Modules.Notifications.Application.Notifications.NotificationPage")).First(type => type is not null)!;
            var responseType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("Sanad.Modules.Notifications.Application.Notifications.NotificationResponse")).First(type => type is not null)!;
            var pageItems = Activator.CreateInstance(typeof(List<>).MakeGenericType(responseType))!;
            var page = Activator.CreateInstance(pageType, [pageItems, null, 0]);
            var result = typeof(Result<>).MakeGenericType(pageType).GetMethod("Success")!.Invoke(null, [page]);
            return Task.FromResult((TResponse)result!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
