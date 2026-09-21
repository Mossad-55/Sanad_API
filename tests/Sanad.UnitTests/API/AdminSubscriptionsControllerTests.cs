using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.UnitTests.API;

public sealed class AdminSubscriptionsControllerTests
{
    [Fact]
    public void Requires_the_super_admin_subscription_policy()
    {
        var authorization = Assert.Single(typeof(AdminSubscriptionsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.SubscriptionPlanAdmin, authorization.Policy);
    }

    [Fact]
    public async Task Dispatches_route_and_authenticated_actor_without_client_actor_fields()
    {
        var actor = UserId.New();
        var sender = new CapturingSender(Result.Success());
        var controller = CreateController(sender, actor);
        var planId = Guid.NewGuid();

        var result = await controller.RetirePlan(planId, default);

        Assert.IsType<NoContentResult>(result);
        var command = Assert.IsType<RetireSubscriptionPlanCommand>(sender.LastRequest);
        Assert.Equal(planId, command.PlanVersionId);
        Assert.Equal(actor, command.ActorUserId);
    }

    [Fact]
    public async Task Maps_missing_to_404_and_retirement_conflicts_to_409()
    {
        var controller = CreateController(new CapturingSender(
            Result.Failure(new Error("Subscriptions.Plan.NotFound", "missing"))), UserId.New());
        var missing = await controller.RetirePlan(Guid.NewGuid(), default);
        Assert.Equal(StatusCodes.Status404NotFound, Assert.IsType<ObjectResult>(missing).StatusCode);

        controller = CreateController(new CapturingSender(
            Result.Failure(new Error("Subscriptions.Plan.AlreadyRetired", "retired"))), UserId.New());
        var conflict = await controller.RetirePlan(Guid.NewGuid(), default);
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(conflict).StatusCode);
    }

    [Fact]
    public async Task Creates_plan_from_terms_and_publishes_draft()
    {
        var sender = new CapturingSender(Result<Guid>.Success(Guid.NewGuid()));
        var controller = CreateController(sender, UserId.New());
        var request = new CreateSubscriptionPlanRequest(
            "managed", 1, 299m, SubscriptionCycle.Monthly, "EGP",
            Enum.GetValues<SubscriptionBenefitKey>()
                .Select(key => new SubscriptionBenefitRequest(key, true))
                .ToArray(),
            new SubscriptionLimitRequest(SubscriptionLimitKind.Finite, 10),
            new SubscriptionLimitRequest(SubscriptionLimitKind.Finite, 20),
            SubscriptionRollover.None);

        var created = await controller.CreatePlan(request, default);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(created).StatusCode);
        Assert.IsType<CreateSubscriptionPlanVersionCommand>(sender.LastRequest);

        sender = new CapturingSender(Result.Success());
        controller = CreateController(sender, UserId.New());
        var published = await controller.PublishPlan(Guid.NewGuid(), default);

        Assert.IsType<NoContentResult>(published);
        Assert.IsType<PublishSubscriptionPlanVersionCommand>(sender.LastRequest);
    }

    private static AdminSubscriptionsController CreateController(ISender sender, UserId? actor = null)
    {
        var controller = new AdminSubscriptionsController(sender)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        if (actor.HasValue)
        {
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNames.Sub, actor.Value.Value.ToString())], "test"));
        }
        return controller;
    }

    private sealed class CapturingSender(object response) : ISender
    {
        public object? LastRequest { get; private set; }
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)response);
        }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
