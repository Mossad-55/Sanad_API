using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.UnitTests.API;

/// <summary>
/// FUR-07 report-intake HTTP contract. The route requires an authenticated
/// Community user; the reporter comes from the JWT (never the body); own-post
/// and non-published targets are 409 conflicts, unknown posts are 404, and a
/// malformed reason is 400.
/// </summary>
public sealed class CommunityPostReportsTests
{
    [Fact]
    public void Controller_UsesAuthenticatedCommunityRoute()
    {
        AuthorizeAttribute authorize =
            Assert.Single(typeof(CommunityPostsController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Null(authorize.Policy);
        Assert.Equal(
            "api/v1/community",
            typeof(CommunityPostsController).GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(
            "posts/{postId:guid}/reports",
            TemplateOf(nameof(CommunityPostsController.ReportPost)));
    }

    [Fact]
    public async Task ReportPost_WithoutSubjectClaim_ReturnsUnauthorized()
    {
        var sender = new StubSender
        {
            Response = Result<SubmitCommunityPostReportResponse>.Success(
                new SubmitCommunityPostReportResponse(Guid.CreateVersion7(), true))
        };
        var controller = CreateController(sender, subject: null);

        IActionResult result = await controller.ReportPost(
            Guid.CreateVersion7(),
            new SubmitCommunityPostReportRequest(CommunityReportReasonCodes.Spam, null),
            default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task ReportPost_ForwardsAuthenticatedReporterAndReturnsReport()
    {
        Guid reporterId = Guid.CreateVersion7();
        Guid reportId = Guid.CreateVersion7();
        Guid postId = Guid.CreateVersion7();
        var sender = new StubSender
        {
            Response = Result<SubmitCommunityPostReportResponse>.Success(
                new SubmitCommunityPostReportResponse(reportId, true))
        };
        var controller = CreateController(sender, reporterId);

        IActionResult result = await controller.ReportPost(
            postId,
            new SubmitCommunityPostReportRequest(CommunityReportReasonCodes.Other, "context"),
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(
            new SubmitCommunityPostReportResponse(reportId, true),
            Assert.IsType<SubmitCommunityPostReportResponse>(ok.Value));
        var command = Assert.IsType<SubmitCommunityPostReportCommand>(sender.LastRequest);
        Assert.Equal(postId, command.PostId);
        Assert.Equal(reporterId, command.ReporterId.Value);
        Assert.Equal(CommunityReportReasonCodes.Other, command.ReasonCode);
        Assert.Equal("context", command.Note);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReportPost_OwnOrUnpublishedPost_ReturnsConflict(bool own)
    {
        Error error = own
            ? CommunityModerationErrors.CannotReportOwnPost
            : CommunityModerationErrors.ReportNotAllowed;
        var sender = new StubSender
        {
            Response = Result<SubmitCommunityPostReportResponse>.Failure(error)
        };
        var controller = CreateController(sender, Guid.CreateVersion7());

        IActionResult result = await controller.ReportPost(
            Guid.CreateVersion7(),
            new SubmitCommunityPostReportRequest(CommunityReportReasonCodes.Spam, null),
            default);

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, objectResult.StatusCode);
        Assert.Equal(StatusCodes.Status409Conflict,
            Assert.IsType<ProblemDetails>(objectResult.Value).Status);
    }

    [Fact]
    public async Task ReportPost_UnknownPost_ReturnsNotFound()
    {
        var sender = new StubSender
        {
            Response = Result<SubmitCommunityPostReportResponse>.Failure(
                new Error("Community.PostNotFound", "Community post not found."))
        };
        var controller = CreateController(sender, Guid.CreateVersion7());

        IActionResult result = await controller.ReportPost(
            Guid.CreateVersion7(),
            new SubmitCommunityPostReportRequest(CommunityReportReasonCodes.Spam, null),
            default);

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
    }

    [Theory]
    [InlineData("Not/Real")]
    [InlineData(null)]
    public async Task ReportPost_InvalidReason_ReturnsBadRequest(string? reasonCode)
    {
        var sender = new StubSender
        {
            Response = Result<SubmitCommunityPostReportResponse>.Failure(
                CommunityModerationErrors.InvalidReportReason)
        };
        var controller = CreateController(sender, Guid.CreateVersion7());

        IActionResult result = await controller.ReportPost(
            Guid.CreateVersion7(),
            new SubmitCommunityPostReportRequest(reasonCode, null),
            default);

        ObjectResult objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
    }

    private static string? TemplateOf(string methodName) =>
        typeof(CommunityPostsController)
            .GetMethod(methodName)!
            .GetCustomAttributes<HttpMethodAttribute>()
            .Single()
            .Template;

    private static CommunityPostsController CreateController(StubSender sender, Guid? subject)
    {
        return new CommunityPostsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(
                            subject is { } value && value != Guid.Empty
                                ? [new System.Security.Claims.Claim(
                                    System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                                    value.ToString("D"))]
                                : [],
                            "Test"))
                }
            }
        };
    }

    private sealed class StubSender : ISender
    {
        public object? LastRequest { get; private set; }

        public object? Response { get; init; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)Response!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
