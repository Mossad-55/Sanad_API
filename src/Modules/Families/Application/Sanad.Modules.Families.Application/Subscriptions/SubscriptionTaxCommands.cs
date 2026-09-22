using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Subscriptions;

public sealed record CreateSubscriptionTaxRuleCommand(
    decimal RatePercentage,
    int Version,
    DateTime EffectiveOnUtc,
    UserId ActorUserId) : ICommand<Guid>;

public sealed record GetCurrentSubscriptionTaxRuleQuery : IQuery<SubscriptionTaxRuleResponse?>;

public sealed record GetSubscriptionTaxRuleHistoryQuery : IQuery<IReadOnlyList<SubscriptionTaxRuleResponse>>;

public sealed record SubscriptionTaxRuleResponse(
    Guid Id,
    decimal RatePercentage,
    int Version,
    DateTime EffectiveOnUtc,
    DateTime CreatedOnUtc,
    bool IsActive);

public sealed class CreateSubscriptionTaxRuleCommandHandler
    : ICommandHandler<CreateSubscriptionTaxRuleCommand, Guid>
{
    private static readonly Error Invalid = new(
        "Subscriptions.Tax.Invalid",
        "Subscription tax rule configuration is invalid.");
    private static readonly Error DuplicateVersion = new(
        "Subscriptions.Tax.DuplicateVersion",
        "A subscription tax rule with this version already exists.");
    private static readonly Error ActiveConflict = new(
        "Subscriptions.Tax.ActiveConflict",
        "The active subscription tax rule changed concurrently.");

    private readonly IFamiliesDbContext _dbContext;

    public CreateSubscriptionTaxRuleCommandHandler(IFamiliesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<Guid>> Handle(
        CreateSubscriptionTaxRuleCommand request,
        CancellationToken cancellationToken)
    {
        if (await _dbContext.SubscriptionTaxRules.AnyAsync(
                item => item.Version == request.Version,
                cancellationToken))
            return Result<Guid>.Failure(DuplicateVersion);

        var activeRule = await _dbContext.SubscriptionTaxRules
            .SingleOrDefaultAsync(item => item.IsActive, cancellationToken);

        try
        {
            var taxRule = SubscriptionTaxRule.Create(
                request.RatePercentage,
                request.Version,
                request.EffectiveOnUtc);

            activeRule?.Deactivate();
            _dbContext.SubscriptionTaxRules.Add(taxRule);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Result<Guid>.Success(taxRule.Id);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<Guid>.Failure(ActiveConflict);
        }
        catch (DbUpdateException exception) when (exception.ToString().Contains(
            "ux_subscription_tax_rules_version", StringComparison.Ordinal))
        {
            return Result<Guid>.Failure(DuplicateVersion);
        }
        catch (DbUpdateException exception) when (exception.ToString().Contains(
            "ux_subscription_tax_rules_active", StringComparison.Ordinal))
        {
            return Result<Guid>.Failure(ActiveConflict);
        }
        catch (DomainException exception)
        {
            return Result<Guid>.Failure(new Error(Invalid.Code, exception.Message));
        }
    }
}

public sealed class GetCurrentSubscriptionTaxRuleQueryHandler
    : IQueryHandler<GetCurrentSubscriptionTaxRuleQuery, SubscriptionTaxRuleResponse?>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetCurrentSubscriptionTaxRuleQueryHandler(IFamiliesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<SubscriptionTaxRuleResponse?>> Handle(
        GetCurrentSubscriptionTaxRuleQuery request,
        CancellationToken cancellationToken)
    {
        var taxRule = await _dbContext.SubscriptionTaxRules
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.IsActive, cancellationToken);

        return Map(taxRule);
    }

    private static SubscriptionTaxRuleResponse? Map(SubscriptionTaxRule? taxRule) =>
        taxRule is null ? null : new(
            taxRule.Id,
            taxRule.RatePercentage,
            taxRule.Version,
            taxRule.EffectiveOnUtc,
            taxRule.CreatedOnUtc,
            taxRule.IsActive);
}

public sealed class GetSubscriptionTaxRuleHistoryQueryHandler
    : IQueryHandler<GetSubscriptionTaxRuleHistoryQuery, IReadOnlyList<SubscriptionTaxRuleResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetSubscriptionTaxRuleHistoryQueryHandler(IFamiliesDbContext dbContext) => _dbContext = dbContext;

    public async Task<Result<IReadOnlyList<SubscriptionTaxRuleResponse>>> Handle(
        GetSubscriptionTaxRuleHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var taxRules = await _dbContext.SubscriptionTaxRules
            .AsNoTracking()
            .OrderByDescending(item => item.Version)
            .ToListAsync(cancellationToken);

        return taxRules
            .Select(item => new SubscriptionTaxRuleResponse(
                item.Id,
                item.RatePercentage,
                item.Version,
                item.EffectiveOnUtc,
                item.CreatedOnUtc,
                item.IsActive))
            .ToList();
    }
}
