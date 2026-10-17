using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.UnitTests.API;

/// <summary>
/// FUR-07 Community-only timed author-restriction HTTP contract. Restrictions
/// are created and revoked under the existing Community moderation policy
/// only; there is no route that changes global Identity status, login,
/// sessions, or another module.
/// </summary>
public sealed class CommunityAuthorRestrictionsTests
{
    private static readonly DateTime StartsOnUtc =
        new(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RestrictionRoutes_UseCommunityModerationPolicy()
    {
        var authorize = typeof(AdminCommunityPostsController)
            .GetCustomAttribute<AuthorizeAttribute>();
        Assert.Equal(AuthorizationPolicies.CommunityModeration, authorize!.Policy);

        Assert.Equal(
            "/api/v1/admin/community/authors/{authorId:guid}/restrictions",
            TemplateOf(nameof(AdminCommunityPostsController.CreateRestriction)));
        Assert.Equal(
            "/api/v1/admin/community/restrictions/{restrictionId:guid}/revoke",
            TemplateOf(nameof(AdminCommunityPostsController.RevokeRestriction)));
    }

    [Fact]
    public void Controller_ExposesNoGlobalIdentitySuspensionRoute()
    {
        string[] templates = typeof(AdminCommunityPostsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .Select(attribute => attribute.Template ?? string.Empty)
            .ToArray();

        Assert.NotEmpty(templates);
        Assert.DoesNotContain(
            templates,
            template =>
                template.Contains("suspend", StringComparison.OrdinalIgnoreCase) ||
                template.Contains("block", StringComparison.OrdinalIgnoreCase) ||
                template.Contains("identity", StringComparison.OrdinalIgnoreCase) ||
                template.Contains("users", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateRestriction_ForwardsAuthorDurationReasonAndNote()
    {
        Guid authorId = Guid.CreateVersion7();
        var response = new CommunityAuthorRestrictionResponse(
            Guid.CreateVersion7(), authorId, Guid.CreateVersion7(),
            nameof(CommunityRestrictionDuration.Days7),
            CommunityRestrictionReasonCodes.UnsafeContent, "unsafe",
            StartsOnUtc, StartsOnUtc.AddDays(7), null, null, null, true);
        var sender = new StubSender
        {
            Response = Result<CommunityAuthorRestrictionResponse>.Success(response)
        };
        var controller = CreateController(sender);

        IActionResult result = await controller.CreateRestriction(
            authorId,
            new CreateCommunityAuthorRestrictionRequest(
                nameof(CommunityRestrictionDuration.Days7),
                CommunityRestrictionReasonCodes.UnsafeContent,
                "unsafe"),
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
        var command = Assert.IsType<CreateCommunityAuthorRestrictionCommand>(sender.LastRequest);
        Assert.Equal(authorId, command.AuthorId);
        Assert.Equal(nameof(CommunityRestrictionDuration.Days7), command.Duration);
        Assert.Equal(CommunityRestrictionReasonCodes.UnsafeContent, command.ReasonCode);
        Assert.Equal("unsafe", command.Note);
    }

    [Fact]
    public async Task CreateRestriction_ValidationFailures_ReturnBadRequest()
    {
        foreach (Error error in new[]
                 {
                     CommunityModerationErrors.InvalidRestrictionDuration,
                     CommunityModerationErrors.InvalidRestrictionReason
                 })
        {
            var sender = new StubSender
            {
                Response = Result<CommunityAuthorRestrictionResponse>.Failure(error)
            };
            IActionResult result = await CreateController(sender).CreateRestriction(
                Guid.CreateVersion7(),
                new CreateCommunityAuthorRestrictionRequest("Bogus", "Not/Real", null),
                default);

            Assert.Equal(StatusCodes.Status400BadRequest, ProblemStatus(result));
        }
    }

    [Fact]
    public async Task CreateRestriction_SecondActive_ReturnsConflict()
    {
        var sender = new StubSender
        {
            Response = Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.RestrictionAlreadyActive)
        };
        var controller = CreateController(sender);

        IActionResult result = await controller.CreateRestriction(
            Guid.CreateVersion7(),
            new CreateCommunityAuthorRestrictionRequest(
                nameof(CommunityRestrictionDuration.Hours24),
                CommunityRestrictionReasonCodes.Spam,
                null),
            default);

        Assert.Equal(StatusCodes.Status409Conflict, ProblemStatus(result));
    }

    [Fact]
    public async Task RevokeRestriction_ForwardsRestrictionIdAndReason()
    {
        Guid restrictionId = Guid.CreateVersion7();
        var response = new CommunityAuthorRestrictionResponse(
            restrictionId, Guid.CreateVersion7(), Guid.CreateVersion7(),
            nameof(CommunityRestrictionDuration.Hours24),
            CommunityRestrictionReasonCodes.Spam, null,
            StartsOnUtc, StartsOnUtc.AddHours(24),
            Guid.CreateVersion7(), StartsOnUtc.AddHours(2), "resolved", false);
        var sender = new StubSender
        {
            Response = Result<CommunityAuthorRestrictionResponse>.Success(response)
        };
        var controller = CreateController(sender);

        IActionResult result = await controller.RevokeRestriction(
            restrictionId, new RevokeCommunityAuthorRestrictionRequest("resolved"), default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
        var command = Assert.IsType<RevokeCommunityAuthorRestrictionCommand>(sender.LastRequest);
        Assert.Equal(restrictionId, command.RestrictionId);
        Assert.Equal("resolved", command.Reason);
    }

    [Fact]
    public async Task RevokeRestriction_BlankReason_ReturnsBadRequest()
    {
        var sender = new StubSender
        {
            Response = Result<CommunityAuthorRestrictionResponse>.Failure(
                CommunityModerationErrors.InvalidRevocationReason)
        };
        var controller = CreateController(sender);

        IActionResult result = await controller.RevokeRestriction(
            Guid.CreateVersion7(), new RevokeCommunityAuthorRestrictionRequest("  "), default);

        Assert.Equal(StatusCodes.Status400BadRequest, ProblemStatus(result));
    }

    private static string? TemplateOf(string methodName) =>
        typeof(AdminCommunityPostsController)
            .GetMethod(methodName)!
            .GetCustomAttributes<HttpMethodAttribute>()
            .Single()
            .Template;

    private static AdminCommunityPostsController CreateController(StubSender sender) =>
        new(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(
                            [new System.Security.Claims.Claim(
                                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                                Guid.CreateVersion7().ToString("D"))],
                            "Test"))
                }
            }
        };

    private static int? ProblemStatus(IActionResult result)
    {
        var objectResult = Assert.IsType<ObjectResult>(result);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        return problem.Status;
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
