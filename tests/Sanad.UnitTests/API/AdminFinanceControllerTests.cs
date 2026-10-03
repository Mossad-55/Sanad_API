using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Finance.Application;

namespace Sanad.UnitTests.API;

public sealed class AdminFinanceControllerTests
{
    [Fact]
    public void Requires_finance_operational_admin_policy_and_exposes_charge_routes()
    {
        var authorization = Assert.Single(typeof(AdminFinanceController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.FinanceOperationalAdmin, authorization.Policy);
        Assert.Equal("api/v1/admin/finance", Assert.Single(typeof(AdminFinanceController).GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("platform-charge-rules", typeof(AdminFinanceController).GetMethod(nameof(AdminFinanceController.Create))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("platform-charge-rules/current", typeof(AdminFinanceController).GetMethod(nameof(AdminFinanceController.Current))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("platform-charge-rules/history", typeof(AdminFinanceController).GetMethod(nameof(AdminFinanceController.History))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public async Task Creates_rule_with_all_rate_and_version_fields()
    {
        var id = Guid.NewGuid();
        var sender = new CapturingSender(Result<Guid>.Success(id));
        var controller = new AdminFinanceController(sender);
        var effective = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        var result = await controller.Create(new(12.5m, 7.25m, 3, effective), default);

        Assert.Equal(201, Assert.IsType<ObjectResult>(result).StatusCode);
        var command = Assert.IsType<CreatePlatformChargeRuleCommand>(sender.LastRequest);
        Assert.Equal((12.5m, 7.25m, 3, effective), (command.PlatformFeeRatePercentage, command.TaxRatePercentage, command.Version, command.EffectiveOnUtc));
    }

    [Fact]
    public async Task Maps_duplicate_or_invalid_create_to_conflict_without_hiding_the_error()
    {
        var error = new Error("Finance.Charges.DuplicateVersion", "duplicate");
        var result = await new AdminFinanceController(new CapturingSender(Result<Guid>.Failure(error)))
            .Create(new(12m, 7m, 2, DateTime.UtcNow), default);

        var response = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(409, response.StatusCode);
        Assert.Same(error, response.Value);
    }

    [Fact]
    public async Task Maps_invalid_finance_configuration_to_bad_request()
    {
        var error = new Error("Finance.Charges.Invalid", "invalid");
        var result = await new AdminFinanceController(new CapturingSender(Result<Guid>.Failure(error)))
            .Create(new(101m, 7m, 2, DateTime.UtcNow), default);

        var response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, response.StatusCode);
        Assert.Same(error, response.Value);
    }

    [Fact]
    public async Task Current_and_history_dispatch_their_read_queries()
    {
        var current = await new AdminFinanceController(new CapturingSender(Result<PlatformChargeRuleResponse?>.Success(null))).Current(default);
        Assert.Null(Assert.IsType<OkObjectResult>(current).Value);

        var history = new List<PlatformChargeRuleResponse>();
        var sender = new CapturingSender(Result<IReadOnlyList<PlatformChargeRuleResponse>>.Success(history));
        var response = await new AdminFinanceController(sender).History(default);
        Assert.Same(history, Assert.IsType<OkObjectResult>(response).Value);
        Assert.IsType<GetPlatformChargeRuleHistoryQuery>(sender.LastRequest);
    }

    private sealed class CapturingSender(object response) : MediatR.ISender
    {
        public object? LastRequest { get; private set; }
        public Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)
        { LastRequest = request; return Task.FromResult((TResponse)response); }
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : MediatR.IRequest { throw new NotSupportedException(); }
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
