using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.UnitTests.API;

public sealed class FamilySubscriptionsControllerTests
{
    [Fact]
    public void Uses_family_access_and_owner_read_routes()
    {
        var authorization = Assert.Single(typeof(FamilySubscriptionsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        var route = Assert.Single(typeof(FamilySubscriptionsController).GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>());
        Assert.Equal(AuthorizationPolicies.FamilyAccess, authorization.Policy);
        Assert.Equal("api/v1/family/subscriptions", route.Template);
        Assert.NotNull(typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.GetPlans)));
        Assert.NotNull(typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.GetCurrent)));
        Assert.NotNull(typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.Quote)));
        Assert.NotNull(typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.CancelRenewal)));
        Assert.NotNull(typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.ReenableAutoRenew)));
    }

    [Fact]
    public void Current_route_declares_nullable_envelope_success_response()
    {
        var method = typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.GetCurrent));
        var response = Assert.Single(method!.GetCustomAttributes<ProducesResponseTypeAttribute>(), x => x.StatusCode == StatusCodes.Status200OK);
        Assert.Equal(typeof(CurrentSubscriptionResponse), response.Type);
    }

    [Fact]
    public void Quote_route_declares_expected_success_and_error_responses()
    {
        var method = typeof(FamilySubscriptionsController).GetMethod(nameof(FamilySubscriptionsController.Quote));
        var route = Assert.Single(method!.GetCustomAttributes<HttpPostAttribute>());
        var responses = method!.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .ToDictionary(attribute => attribute.StatusCode);

        Assert.Equal("quote", route.Template);
        Assert.Equal(typeof(SubscriptionQuoteResponse), responses[StatusCodes.Status200OK].Type);
        Assert.Contains(StatusCodes.Status400BadRequest, responses.Keys);
        Assert.Contains(StatusCodes.Status403Forbidden, responses.Keys);
        Assert.Contains(StatusCodes.Status404NotFound, responses.Keys);
        Assert.Contains(StatusCodes.Status409Conflict, responses.Keys);
    }

    // ------------------ E04-B platform control enforcement ------------------

    [Fact]
    public void Payment_intent_routes_declare_the_platform_control_failures()
    {
        AssertDeclaresPlatformControlFailures(
            nameof(FamilySubscriptionsController.CreatePaymentIntent),
            "payment-intent");
        AssertDeclaresPlatformControlFailures(
            nameof(FamilySubscriptionsController.CreateRenewalPaymentIntent),
            "renewal/payment-intent");
        AssertDeclaresPlatformControlFailures(
            nameof(FamilySubscriptionsController.CreatePlanChangePaymentIntent),
            "plan-change/payment-intent");
    }

    [Theory]
    [InlineData("Platform.Maintenance", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("Platform.ControlStateUnavailable", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("Platform.PaymentMethodUnavailable", StatusCodes.Status400BadRequest)]
    public async Task Subscription_payment_intent_maps_platform_controls_to_documented_statuses(
        string code,
        int expectedStatus)
    {
        var controller = CreateController(new FailingPlatformSender(new Error(code, "blocked by platform controls")));

        var result = await controller.CreatePaymentIntent(
            new CreateSubscriptionPaymentIntentRequest(
                Guid.NewGuid(),
                null,
                SubscriptionPaymentMethod.Card,
                Billing()),
            CancellationToken.None);

        AssertPlatformStatus(result, expectedStatus, code);
    }

    [Theory]
    [InlineData("Platform.Maintenance", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("Platform.ControlStateUnavailable", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("Platform.PaymentMethodUnavailable", StatusCodes.Status400BadRequest)]
    public async Task Renewal_payment_intent_maps_platform_controls_to_documented_statuses(
        string code,
        int expectedStatus)
    {
        var controller = CreateController(new FailingPlatformSender(new Error(code, "blocked by platform controls")));

        var result = await controller.CreateRenewalPaymentIntent(
            new CreateSubscriptionRenewalPaymentIntentRequest(SubscriptionPaymentMethod.Wallet, Billing()),
            CancellationToken.None);

        AssertPlatformStatus(result, expectedStatus, code);
    }

    [Theory]
    [InlineData("Platform.Maintenance", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("Platform.ControlStateUnavailable", StatusCodes.Status503ServiceUnavailable)]
    [InlineData("Platform.PaymentMethodUnavailable", StatusCodes.Status400BadRequest)]
    public async Task Plan_change_payment_intent_maps_platform_controls_to_documented_statuses(
        string code,
        int expectedStatus)
    {
        // The plan-change helper falls back to 409 for its own unknown codes;
        // the platform outcomes must still surface as 503/400.
        var controller = CreateController(new FailingPlatformSender(new Error(code, "blocked by platform controls")));

        var result = await controller.CreatePlanChangePaymentIntent(
            new CreatePlanChangePaymentIntentRequest(Guid.NewGuid(), SubscriptionPaymentMethod.Card, Billing()),
            CancellationToken.None);

        AssertPlatformStatus(result, expectedStatus, code);
    }

    [Theory]
    [InlineData("Subscriptions.PlanChange.PaidPeriodActive")]
    [InlineData("Subscriptions.PlanChange.PaymentPending")]
    public async Task Plan_change_payment_intent_maps_the_paid_period_policy_to_documented_409(string code)
    {
        // Mobile integration 2026-10-26: the no-plan-change-during-paid-period
        // policy and its duplicate-attempt guard are documented conflicts.
        var controller = CreateController(new FailingPlatformSender(new Error(code, "refused")));

        var result = await controller.CreatePlanChangePaymentIntent(
            new CreatePlanChangePaymentIntentRequest(Guid.NewGuid(), SubscriptionPaymentMethod.Card, Billing()),
            CancellationToken.None);

        AssertPlatformStatus(result, StatusCodes.Status409Conflict, code);
    }

    // ------------------ W4 payment history ------------------

    [Fact]
    public void Payment_history_routes_declare_the_documented_contract()
    {
        MethodInfo list = typeof(FamilySubscriptionsController)
            .GetMethod(nameof(FamilySubscriptionsController.GetPayments))!;
        MethodInfo detail = typeof(FamilySubscriptionsController)
            .GetMethod(nameof(FamilySubscriptionsController.GetPayment))!;

        Assert.Equal("payments", Assert.Single(list.GetCustomAttributes<HttpGetAttribute>()).Template);
        Assert.Equal(
            "payments/{paymentAttemptId:guid}",
            Assert.Single(detail.GetCustomAttributes<HttpGetAttribute>()).Template);

        var listResponses = list.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .ToDictionary(attribute => attribute.StatusCode);
        Assert.Equal(typeof(SubscriptionPaymentPageResponse), listResponses[StatusCodes.Status200OK].Type);
        Assert.Contains(StatusCodes.Status400BadRequest, listResponses.Keys);
        Assert.Contains(StatusCodes.Status403Forbidden, listResponses.Keys);

        var detailResponses = detail.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .ToDictionary(attribute => attribute.StatusCode);
        Assert.Equal(typeof(SubscriptionPaymentResponse), detailResponses[StatusCodes.Status200OK].Type);
        Assert.Contains(StatusCodes.Status403Forbidden, detailResponses.Keys);
        Assert.Contains(StatusCodes.Status404NotFound, detailResponses.Keys);
    }

    [Theory]
    [InlineData("Subscriptions.Payment.NotOwner", StatusCodes.Status403Forbidden)]
    [InlineData("Subscriptions.Payment.InvalidQuery", StatusCodes.Status400BadRequest)]
    public async Task Payment_history_maps_owner_and_validation_failures(
        string code,
        int expectedStatus)
    {
        var controller = CreateController(
            new StubSender<SubscriptionPaymentPageResponse>(
                Result<SubscriptionPaymentPageResponse>.Failure(new Error(code, "rejected"))));

        var result = await controller.GetPayments(
            page: 1,
            pageSize: 20,
            status: null,
            method: null,
            paidFromUtc: null,
            paidToUtc: null,
            CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData("Subscriptions.Payment.NotOwner", StatusCodes.Status403Forbidden)]
    [InlineData("Subscriptions.Payment.NotFound", StatusCodes.Status404NotFound)]
    public async Task Payment_detail_maps_owner_and_not_found_failures(
        string code,
        int expectedStatus)
    {
        var controller = CreateController(
            new StubSender<SubscriptionPaymentResponse>(
                Result<SubscriptionPaymentResponse>.Failure(new Error(code, "rejected"))));

        var result = await controller.GetPayment(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(expectedStatus, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task Payment_history_and_detail_return_the_success_envelopes()
    {
        DateTime settledOnUtc = DateTime.UtcNow;
        var payment = new SubscriptionPaymentResponse(
            Guid.NewGuid(),
            340.86m,
            "EGP",
            SubscriptionPaymentAttemptStatus.Succeeded,
            settledOnUtc,
            Guid.NewGuid(),
            SubscriptionPaymentMethod.Wallet,
            settledOnUtc.AddMinutes(-5),
            "premium",
            1,
            IsRenewal: false,
            IsPlanChange: false);
        var page = new SubscriptionPaymentPageResponse([payment], 1, 20, 1, 1);

        var list = await CreateController(
                new StubSender<SubscriptionPaymentPageResponse>(
                    Result<SubscriptionPaymentPageResponse>.Success(page)))
            .GetPayments(1, 20, null, null, null, null, CancellationToken.None);
        var detail = await CreateController(
                new StubSender<SubscriptionPaymentResponse>(
                    Result<SubscriptionPaymentResponse>.Success(payment)))
            .GetPayment(payment.PaymentAttemptId, CancellationToken.None);

        var listOk = Assert.IsType<OkObjectResult>(list);
        Assert.Same(page, listOk.Value);
        var detailOk = Assert.IsType<OkObjectResult>(detail);
        Assert.Same(payment, detailOk.Value);
    }

    private static PaymobBillingData Billing() =>
        new("Fixture", "Family", "family@test.local", "+201000000000");

    private static void AssertDeclaresPlatformControlFailures(string actionName, string template)
    {
        var method = typeof(FamilySubscriptionsController).GetMethod(actionName);
        Assert.NotNull(method);
        var routes = method!.GetCustomAttributes<HttpPostAttribute>().ToArray();
        Assert.Contains(routes, route => route.Template == template);

        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .ToDictionary(attribute => attribute.StatusCode);

        Assert.Contains(StatusCodes.Status400BadRequest, responses.Keys);
        Assert.Contains(StatusCodes.Status503ServiceUnavailable, responses.Keys);
    }

    private static void AssertPlatformStatus(IActionResult result, int expectedStatus, string code)
    {
        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatus, status.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(status.Value);
        Assert.Equal(code, problem.Extensions["code"]);
    }

    [Fact]
    public async Task Plan_catalog_response_carries_the_version_owned_bilingual_copy()
    {
        var catalog = new List<SubscriptionPlanResponse>
        {
            new(
                Guid.NewGuid(),
                "premium",
                2,
                299m,
                SubscriptionCycle.Monthly,
                "EGP",
                new(SubscriptionLimitKind.Finite, 10),
                new(SubscriptionLimitKind.Finite, 20),
                SubscriptionRollover.None,
                true,
                DateTime.UtcNow,
                DateTime.UtcNow,
                [
                    new(SubscriptionBenefitKey.Chatting, true, "محادثات", "Chatting")
                ],
                null,
                "الباقة المميزة",
                "Premium plan",
                "كل ما تتضمنه الباقة.",
                "Everything the plan includes.")
        };
        var controller = CreateController(new CatalogSender(catalog));

        var result = await controller.GetPlans(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var items = Assert.IsAssignableFrom<IReadOnlyList<SubscriptionPlanResponse>>(ok.Value);
        SubscriptionPlanResponse item = Assert.Single(items);
        Assert.Equal("الباقة المميزة", item.ArabicName);
        Assert.Equal("Premium plan", item.EnglishName);
        Assert.Equal("كل ما تتضمنه الباقة.", item.ArabicDescription);
        Assert.Equal("Everything the plan includes.", item.EnglishDescription);
        Assert.Equal("محادثات", Assert.Single(item.Benefits).ArabicLabel);
        Assert.Equal("Chatting", Assert.Single(item.Benefits).EnglishLabel);
    }

    private static FamilySubscriptionsController CreateController(ISender sender)
    {
        var controller = new FamilySubscriptionsController(sender, new StubFileStorage())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
                        authenticationType: "test"))
                }
            }
        };

        return controller;
    }

    private sealed class FailingPlatformSender(Error error) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            Task.FromResult((TResponse)(object)Result<SubscriptionPaymentIntentResponse>.Failure(error));

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CatalogSender(IReadOnlyList<SubscriptionPlanResponse> catalog) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            Task.FromResult((TResponse)(object)Result<IReadOnlyList<SubscriptionPlanResponse>>.Success(catalog));

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubSender<TResponse>(Result<TResponse> result) : ISender
    {
        public Task<T> Send<T>(IRequest<T> request, CancellationToken cancellationToken = default) =>
            Task.FromResult((T)(object)result);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse1> CreateStream<TResponse1>(IStreamRequest<TResponse1> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StubFileStorage : IFileStorage
    {
        public Task<Result<StoredFile>> SaveAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<StoredFile>> SavePrivateAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<PrivateFileContent>> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
