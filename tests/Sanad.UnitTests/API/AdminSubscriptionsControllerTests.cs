using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
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
    public void Exposes_the_three_tax_rule_routes_and_expected_response_metadata()
    {
        var route = Assert.Single(typeof(AdminSubscriptionsController)
            .GetCustomAttributes<RouteAttribute>());
        Assert.Equal("api/v1/admin/subscriptions", route.Template);

        var create = typeof(AdminSubscriptionsController).GetMethod(nameof(AdminSubscriptionsController.CreateTaxRule))!;
        var current = typeof(AdminSubscriptionsController).GetMethod(nameof(AdminSubscriptionsController.GetCurrentTaxRule))!;
        var history = typeof(AdminSubscriptionsController).GetMethod(nameof(AdminSubscriptionsController.GetTaxRuleHistory))!;

        Assert.Equal("tax-rules", create.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("tax-rules/current", current.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("tax-rules/history", history.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Contains(create.GetCustomAttributes<ProducesResponseTypeAttribute>(), x => x.StatusCode == 201);
        Assert.Contains(create.GetCustomAttributes<ProducesResponseTypeAttribute>(), x => x.StatusCode == 400);
        Assert.Contains(create.GetCustomAttributes<ProducesResponseTypeAttribute>(), x => x.StatusCode == 409);
        Assert.Contains(current.GetCustomAttributes<ProducesResponseTypeAttribute>(), x => x.StatusCode == 200);
        Assert.Contains(history.GetCustomAttributes<ProducesResponseTypeAttribute>(), x => x.StatusCode == 200);
    }

    [Fact]
    public void Exposes_admin_subscription_list_and_detail_routes()
    {
        var list = typeof(AdminSubscriptionsController).GetMethod(nameof(AdminSubscriptionsController.ListFamilySubscriptions))!;
        var detail = typeof(AdminSubscriptionsController).GetMethod(nameof(AdminSubscriptionsController.GetFamilySubscription))!;
        var plans = typeof(AdminSubscriptionsController).GetMethod(nameof(AdminSubscriptionsController.ListPlans))!;
        var plan = typeof(AdminSubscriptionsController).GetMethod(nameof(AdminSubscriptionsController.GetPlan))!;

        Assert.Null(list.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{subscriptionId:guid}", detail.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("plans", plans.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("plans/{planVersionId:guid}", plan.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public async Task Dispatches_admin_subscription_read_queries()
    {
        var sender = new CapturingSender(Result<PagedAdminFamilySubscriptions>.Success(
            new([], 1, 10, 0)));
        var controller = CreateController(sender, UserId.New());

        var list = await controller.ListFamilySubscriptions(2, 25, default);

        Assert.IsType<OkObjectResult>(list);
        var listQuery = Assert.IsType<GetAdminFamilySubscriptionsQuery>(sender.LastRequest);
        Assert.Equal(2, listQuery.Page);
        Assert.Equal(25, listQuery.PageSize);

        sender = new CapturingSender(Result<AdminSubscriptionPlanResponse>.Success(
            new(Guid.NewGuid(), "premium", 1, 299m, SubscriptionCycle.Monthly, "EGP",
                new(SubscriptionLimitKind.Finite, 10), new(SubscriptionLimitKind.Finite, 20),
                SubscriptionRollover.None, true, true, DateTime.UtcNow, DateTime.UtcNow, [])));
        controller = CreateController(sender, UserId.New());

        var plan = await controller.GetPlan(Guid.NewGuid(), default);

        Assert.IsType<OkObjectResult>(plan);
        Assert.IsType<GetAdminSubscriptionPlanQuery>(sender.LastRequest);
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

    [Fact]
    public async Task Maps_duplicate_coupon_code_to_409()
    {
        var controller = CreateController(new CapturingSender(
            Result<Guid>.Failure(new Error("Subscriptions.Coupon.DuplicateCode", "duplicate"))), UserId.New());

        var result = await controller.CreateCoupon(
            new CreateSubscriptionCouponRequest("WELCOME", Guid.NewGuid(), 10m, DateTime.UtcNow.AddDays(1)), default);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Creates_tax_rule_with_exact_request_fields_and_authenticated_actor()
    {
        var actor = UserId.New();
        var sender = new CapturingSender(Result<Guid>.Success(Guid.NewGuid()));
        var controller = CreateController(sender, actor);
        var effective = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await controller.CreateTaxRule(new(12.345m, 7, effective), default);

        Assert.Equal(StatusCodes.Status201Created, Assert.IsType<ObjectResult>(result).StatusCode);
        var command = Assert.IsType<CreateSubscriptionTaxRuleCommand>(sender.LastRequest);
        Assert.Equal(12.345m, command.RatePercentage);
        Assert.Equal(7, command.Version);
        Assert.Equal(effective, command.EffectiveOnUtc);
        Assert.Equal(actor, command.ActorUserId);
    }

    [Fact]
    public async Task Current_tax_rule_dispatches_query_and_returns_json_null()
    {
        var sender = new CapturingSender(Result<SubscriptionTaxRuleResponse?>.Success(null));
        var result = await CreateController(sender, UserId.New()).GetCurrentTaxRule(default);

        var response = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Null(response.Value);
        Assert.IsType<GetCurrentSubscriptionTaxRuleQuery>(sender.LastRequest);
    }

    [Fact]
    public async Task History_tax_rule_dispatches_query_and_returns_handler_value()
    {
        var response = new List<SubscriptionTaxRuleResponse>
        {
            new(Guid.NewGuid(), 20m, 2, DateTime.UtcNow, DateTime.UtcNow, true),
            new(Guid.NewGuid(), 10m, 1, DateTime.UtcNow, DateTime.UtcNow, false)
        };
        var sender = new CapturingSender(Result<IReadOnlyList<SubscriptionTaxRuleResponse>>.Success(response));

        var result = await CreateController(sender, UserId.New()).GetTaxRuleHistory(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(response, ok.Value);
        Assert.IsType<GetSubscriptionTaxRuleHistoryQuery>(sender.LastRequest);
    }

    [Fact]
    public async Task Maps_tax_invalid_to_400_and_tax_conflicts_to_409()
    {
        var controller = CreateController(new CapturingSender(
            Result<Guid>.Failure(new Error("Subscriptions.Tax.Invalid", "invalid"))), UserId.New());
        var invalid = await controller.CreateTaxRule(new(101m, 1, DateTime.UtcNow), default);
        var invalidProblem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(invalid).Value);
        Assert.Equal(StatusCodes.Status400BadRequest, invalidProblem.Status);
        Assert.Equal("Subscriptions.Tax.Invalid", invalidProblem.Extensions["code"]);

        controller = CreateController(new CapturingSender(
            Result<Guid>.Failure(new Error("Subscriptions.Tax.DuplicateVersion", "duplicate"))), UserId.New());
        var duplicate = await controller.CreateTaxRule(new(10m, 1, DateTime.UtcNow), default);
        var duplicateProblem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(duplicate).Value);
        Assert.Equal(StatusCodes.Status409Conflict, duplicateProblem.Status);
        Assert.Equal("Subscriptions.Tax.DuplicateVersion", duplicateProblem.Extensions["code"]);

        controller = CreateController(new CapturingSender(
            Result<Guid>.Failure(new Error("Subscriptions.Tax.ActiveConflict", "race"))), UserId.New());
        var race = await controller.CreateTaxRule(new(10m, 2, DateTime.UtcNow), default);
        var raceProblem = Assert.IsType<ProblemDetails>(Assert.IsType<ObjectResult>(race).Value);
        Assert.Equal(StatusCodes.Status409Conflict, raceProblem.Status);
        Assert.Equal("Subscriptions.Tax.ActiveConflict", raceProblem.Extensions["code"]);
    }

    [Fact]
    public async Task Create_tax_rule_requires_an_authenticated_actor()
    {
        var sender = new CapturingSender(Result<Guid>.Success(Guid.NewGuid()));
        var controller = CreateController(sender);

        var result = await controller.CreateTaxRule(new(10m, 1, DateTime.UtcNow), default);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Null(sender.LastRequest);
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
