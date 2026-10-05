using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Finance.Application;

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
    bool IsActive,
    decimal? PlatformFeeRatePercentage = null,
    int? SharedRuleVersion = null,
    bool IsShared = false);

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
    private readonly IPlatformChargeRuleReader _chargeReader;
    private readonly IPlatformChargeRuleWriter _chargeWriter;

    public CreateSubscriptionTaxRuleCommandHandler(IFamiliesDbContext dbContext, IPlatformChargeRuleReader chargeReader, IPlatformChargeRuleWriter chargeWriter)
    {
        _dbContext = dbContext;
        _chargeReader = chargeReader;
        _chargeWriter = chargeWriter;
    }

    public async Task<Result<Guid>> Handle(
        CreateSubscriptionTaxRuleCommand request,
        CancellationToken cancellationToken)
    {
        var sharedRule = await _chargeReader.GetEffectiveAsync(DateTime.UtcNow, cancellationToken);
        if (sharedRule is null)
            return Result<Guid>.Failure(new Error("Subscriptions.Tax.SharedConfigurationRequired", "Initialize both platform fee and tax through Finance before using the legacy tax route."));

        try
        {
            var shared = await _chargeWriter.CreateAsync(sharedRule.PlatformFeeRatePercentage, request.RatePercentage, request.Version, request.EffectiveOnUtc, cancellationToken);
            if (shared.IsFailure)
                return Result<Guid>.Failure(new Error("Subscriptions.Tax.SharedConfigurationConflict", shared.Error.Message));
            return Result<Guid>.Success(shared.Value);
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
    private readonly IPlatformChargeRuleReader _chargeReader;

    public GetCurrentSubscriptionTaxRuleQueryHandler(IFamiliesDbContext dbContext, IPlatformChargeRuleReader chargeReader) { _dbContext = dbContext; _chargeReader = chargeReader; }

    public async Task<Result<SubscriptionTaxRuleResponse?>> Handle(
        GetCurrentSubscriptionTaxRuleQuery request,
        CancellationToken cancellationToken)
    {
        var shared = await _chargeReader.GetEffectiveAsync(DateTime.UtcNow, cancellationToken);
        return shared is null ? null : new SubscriptionTaxRuleResponse(Guid.Empty, shared.TaxRatePercentage, shared.Version, DateTime.UtcNow, DateTime.UtcNow, true, shared.PlatformFeeRatePercentage, shared.Version, true);
    }

    private static SubscriptionTaxRuleResponse? Map(SubscriptionTaxRule? taxRule, PlatformChargeRuleRates? shared) =>
        taxRule is null ? null : new(
            taxRule.Id,
            taxRule.RatePercentage,
            taxRule.Version,
            taxRule.EffectiveOnUtc,
            taxRule.CreatedOnUtc,
            taxRule.IsActive,
            null,
            null,
            false);
}

public sealed class GetSubscriptionTaxRuleHistoryQueryHandler
    : IQueryHandler<GetSubscriptionTaxRuleHistoryQuery, IReadOnlyList<SubscriptionTaxRuleResponse>>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IPlatformChargeRuleReader _chargeReader;

    public GetSubscriptionTaxRuleHistoryQueryHandler(IFamiliesDbContext dbContext, IPlatformChargeRuleReader chargeReader) { _dbContext = dbContext; _chargeReader = chargeReader; }

    public async Task<Result<IReadOnlyList<SubscriptionTaxRuleResponse>>> Handle(
        GetSubscriptionTaxRuleHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var taxRules = await _dbContext.SubscriptionTaxRules
            .AsNoTracking()
            .OrderByDescending(item => item.Version)
            .ToListAsync(cancellationToken);

        var sharedHistory = await _chargeReader.GetHistoryAsync(cancellationToken);
        var authoritative = sharedHistory.Select(item => new SubscriptionTaxRuleResponse(item.Id, item.TaxRatePercentage, item.Version, item.EffectiveOnUtc, item.CreatedOnUtc, item.IsActive, item.PlatformFeeRatePercentage, item.Version, true));
        var legacy = taxRules.Select(item => new SubscriptionTaxRuleResponse(
                item.Id,
                item.RatePercentage,
                item.Version,
                item.EffectiveOnUtc,
                item.CreatedOnUtc,
                item.IsActive,
                null,
                null,
                false));
        return authoritative.Concat(legacy)
            .OrderByDescending(item => item.Version)
            .ToList();
    }
}
