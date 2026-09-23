using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Payments;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Application.Subscriptions;

public sealed record SubscriptionPaymentIntentResponse(
    Guid PaymentAttemptId,
    string MerchantReference,
    SubscriptionPaymentMethod Method,
    decimal Amount,
    string Currency,
    string ClientSecret,
    string PublicKey,
    bool RecurringRenewalSupported);

public sealed record CreateSubscriptionPaymentIntentCommand(
    UserId UserId,
    Guid PlanVersionId,
    string? CouponCode,
    SubscriptionPaymentMethod Method,
    PaymobBillingData Billing,
    DateTime UtcNow) : ICommand<SubscriptionPaymentIntentResponse>;

public sealed record CreateSubscriptionRenewalPaymentIntentCommand(
    UserId UserId,
    SubscriptionPaymentMethod Method,
    PaymobBillingData Billing,
    DateTime UtcNow) : ICommand<SubscriptionPaymentIntentResponse>;

public sealed class CreateSubscriptionPaymentIntentCommandValidator
    : AbstractValidator<CreateSubscriptionPaymentIntentCommand>
{
    public CreateSubscriptionPaymentIntentCommandValidator()
    {
        RuleFor(c => c.Method).IsInEnum();
        RuleFor(c => c.Billing.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Billing.LastName).NotEmpty().MaximumLength(100);
        RuleFor(c => c.Billing.Email).NotEmpty().EmailAddress().MaximumLength(200);
        RuleFor(c => c.Billing.PhoneNumber)
            .NotEmpty()
            .Matches(@"^\+?\d{8,15}$")
            .WithMessage("A valid phone number is required.");
    }
}

public sealed class CreateSubscriptionPaymentIntentCommandHandler
    : ICommandHandler<CreateSubscriptionPaymentIntentCommand, SubscriptionPaymentIntentResponse>
{
    private static readonly Error NotOwner = new("Subscriptions.Payment.NotOwner", "Only the family owner can start a subscription payment.");
    private static readonly Error CurrentExists = new("Subscriptions.Payment.CurrentExists", "A current subscription already exists for this family.");
    private static readonly Error NotRequired = new("Subscriptions.Payment.NotRequired", "A payment is not required for this subscription.");

    private readonly IFamiliesDbContext _db;
    private readonly IPaymobClient _paymobClient;

    public CreateSubscriptionPaymentIntentCommandHandler(IFamiliesDbContext db, IPaymobClient paymobClient)
    {
        _db = db;
        _paymobClient = paymobClient;
    }

    public async Task<Result<SubscriptionPaymentIntentResponse>> Handle(
        CreateSubscriptionPaymentIntentCommand request,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId))
            return Result<SubscriptionPaymentIntentResponse>.Failure(NotOwner);

        if (await _db.FamilySubscriptions.AnyAsync(
                item => item.FamilyId == family.Id && item.IsCurrent,
                cancellationToken))
            return Result<SubscriptionPaymentIntentResponse>.Failure(CurrentExists);

        var quote = await new CreateSubscriptionQuoteQueryHandler(_db).Handle(
            new CreateSubscriptionQuoteCommand(request.UserId, request.PlanVersionId, request.CouponCode, request.UtcNow),
            cancellationToken);
        if (!quote.IsSuccess)
            return Result<SubscriptionPaymentIntentResponse>.Failure(quote.Error);
        if (quote.Value.TotalPayable <= 0m)
            return Result<SubscriptionPaymentIntentResponse>.Failure(NotRequired);

        var plan = await _db.SubscriptionPlanVersions
            .AsNoTracking()
            .SingleAsync(item => item.Id == quote.Value.PlanVersionId, cancellationToken);

        var attempt = SubscriptionPaymentAttempt.Create(
            family.Id,
            plan,
            quote.Value.BasePrice,
            quote.Value.DiscountPercentage,
            quote.Value.DiscountAmount,
            quote.Value.TaxableAmount,
            quote.Value.TaxRatePercentage,
            quote.Value.TaxAmount,
            quote.Value.TotalPayable,
            quote.Value.CouponCode,
            request.Method,
            request.UtcNow);

        Result<PaymobPaymentIntent> intent = await _paymobClient.CreateSubscriptionPaymentIntentAsync(
            new PaymobSubscriptionPaymentIntentInput(
                attempt.MerchantReference,
                request.Method,
                attempt.TotalPayable,
                attempt.Currency,
                request.Billing,
                request.Method == SubscriptionPaymentMethod.Card ? plan.PaymobSubscriptionPlanId : null),
            cancellationToken);

        if (!intent.IsSuccess)
            return Result<SubscriptionPaymentIntentResponse>.Failure(intent.Error);

        attempt.RecordPaymobOrder(intent.Value.PaymobOrderId);
        _db.SubscriptionPaymentAttempts.Add(attempt);
        await _db.SaveChangesAsync(cancellationToken);

        return new SubscriptionPaymentIntentResponse(
            attempt.Id,
            attempt.MerchantReference,
            attempt.Method,
            attempt.TotalPayable,
            attempt.Currency,
            intent.Value.ClientSecret,
            intent.Value.PublicKey,
            RecurringRenewalSupported: intent.Value.RecurringRenewalSupported);
    }
}

public sealed class CreateSubscriptionRenewalPaymentIntentCommandHandler
    : ICommandHandler<CreateSubscriptionRenewalPaymentIntentCommand, SubscriptionPaymentIntentResponse>
{
    private static readonly Error NotOwner = new("Subscriptions.Renewal.NotOwner", "Only the family owner can renew a subscription.");
    private static readonly Error NotFound = new("Subscriptions.Renewal.NotFound", "The current subscription was not found.");
    private static readonly Error NotDue = new("Subscriptions.Renewal.NotDue", "The subscription renewal is not due yet.");
    private static readonly Error GraceExpired = new("Subscriptions.Renewal.GraceExpired", "The subscription renewal grace period has ended.");
    private static readonly Error PendingExists = new("Subscriptions.Renewal.PendingExists", "A renewal payment is already pending.");

    private readonly IFamiliesDbContext _db;
    private readonly IPaymobClient _paymobClient;

    public CreateSubscriptionRenewalPaymentIntentCommandHandler(
        IFamiliesDbContext db,
        IPaymobClient paymobClient)
    {
        _db = db;
        _paymobClient = paymobClient;
    }

    public async Task<Result<SubscriptionPaymentIntentResponse>> Handle(
        CreateSubscriptionRenewalPaymentIntentCommand request,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
        if (family is null || !FamilyAccess.IsOwner(family, request.UserId))
            return Result<SubscriptionPaymentIntentResponse>.Failure(NotOwner);

        var subscription = await _db.FamilySubscriptions
            .SingleOrDefaultAsync(item => item.FamilyId == family.Id && item.IsCurrent, cancellationToken);
        if (subscription is null)
            return Result<SubscriptionPaymentIntentResponse>.Failure(NotFound);
        if (request.UtcNow < subscription.CurrentPeriodEndsOnUtc)
            return Result<SubscriptionPaymentIntentResponse>.Failure(NotDue);
        if (subscription.RenewalGraceEndsOnUtc is not null &&
            request.UtcNow >= subscription.RenewalGraceEndsOnUtc.Value)
            return Result<SubscriptionPaymentIntentResponse>.Failure(GraceExpired);
        if (await _db.SubscriptionPaymentAttempts.AnyAsync(
                item => item.SubscriptionId == subscription.Id &&
                        item.IsRenewal &&
                        item.Status == SubscriptionPaymentAttemptStatus.Pending,
                cancellationToken))
            return Result<SubscriptionPaymentIntentResponse>.Failure(PendingExists);

        var plan = await _db.SubscriptionPlanVersions
            .SingleOrDefaultAsync(
                item => item.Key == subscription.PlanKey && item.Version == subscription.PlanVersion,
                cancellationToken);
        if (plan is null)
            return Result<SubscriptionPaymentIntentResponse>.Failure(
                new Error("Subscriptions.Renewal.PlanNotFound", "The subscription plan for renewal was not found."));

        SubscriptionPaymentAttempt attempt = SubscriptionPaymentAttempt.CreateRenewal(
            subscription,
            plan,
            request.Method,
            request.UtcNow);

        Result<PaymobPaymentIntent> intent = await _paymobClient.CreateSubscriptionPaymentIntentAsync(
            new PaymobSubscriptionPaymentIntentInput(
                attempt.MerchantReference,
                request.Method,
                attempt.TotalPayable,
                attempt.Currency,
                request.Billing),
            cancellationToken);
        if (!intent.IsSuccess)
            return Result<SubscriptionPaymentIntentResponse>.Failure(intent.Error);

        attempt.RecordPaymobOrder(intent.Value.PaymobOrderId);
        _db.SubscriptionPaymentAttempts.Add(attempt);
        await _db.SaveChangesAsync(cancellationToken);

        return new SubscriptionPaymentIntentResponse(
            attempt.Id,
            attempt.MerchantReference,
            attempt.Method,
            attempt.TotalPayable,
            attempt.Currency,
            intent.Value.ClientSecret,
            intent.Value.PublicKey,
            RecurringRenewalSupported: false);
    }
}

public sealed record ConfirmSubscriptionPaymentCommand(
    string MerchantReference,
    long PaymobTransactionId,
    long AmountCents,
    string Currency,
    bool Success,
    bool Pending,
    DateTime UtcNow) : ICommand<ConfirmSubscriptionPaymentResponse>;

public sealed record ConfirmSubscriptionPaymentResponse(Guid PaymentAttemptId, string Outcome);

public sealed class ConfirmSubscriptionPaymentCommandHandler
    : ICommandHandler<ConfirmSubscriptionPaymentCommand, ConfirmSubscriptionPaymentResponse>
{
    private readonly IFamiliesDbContext _db;

    public ConfirmSubscriptionPaymentCommandHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<ConfirmSubscriptionPaymentResponse>> Handle(
        ConfirmSubscriptionPaymentCommand request,
        CancellationToken cancellationToken)
    {
        if (request.MerchantReference.Length <= 4
            || !Guid.TryParseExact(request.MerchantReference[4..], "N", out Guid attemptId))
            return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                new Error("Subscriptions.Payment.NotFound", "No subscription payment matches this provider order."));

        var attempt = await _db.SubscriptionPaymentAttempts
            .SingleOrDefaultAsync(item => item.Id == attemptId, cancellationToken);

        if (attempt is null)
            return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                new Error("Subscriptions.Payment.NotFound", "No subscription payment matches this provider order."));

        if (attempt.Status != SubscriptionPaymentAttemptStatus.Pending)
            return new ConfirmSubscriptionPaymentResponse(attempt.Id, "AlreadyProcessed");

        long expectedCents = (long)decimal.Round(attempt.TotalPayable * 100m, 0, MidpointRounding.ToEven);
        if (expectedCents != request.AmountCents || !string.Equals(attempt.Currency, request.Currency, StringComparison.OrdinalIgnoreCase))
            return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                new Error("Paymob.AmountMismatch", "Webhook amount or currency does not match the recorded subscription payment."));

        string transactionId = request.PaymobTransactionId.ToString();
        if (!request.Success)
        {
            if (request.Pending)
                return new ConfirmSubscriptionPaymentResponse(attempt.Id, "Pending");

            attempt.TryMarkFailed(transactionId, request.UtcNow);
            if (attempt.IsRenewal)
            {
                var failedSubscription = await _db.FamilySubscriptions
                    .SingleOrDefaultAsync(item => item.Id == attempt.SubscriptionId && item.IsCurrent, cancellationToken);
                failedSubscription?.BeginRenewalGrace(request.UtcNow);
            }
            await _db.SaveChangesAsync(cancellationToken);
            return new ConfirmSubscriptionPaymentResponse(attempt.Id, "Failed");
        }

        var plan = await _db.SubscriptionPlanVersions
            .SingleOrDefaultAsync(item => item.Id == attempt.PlanVersionId, cancellationToken);
        if (plan is null)
            return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                new Error("Subscriptions.Payment.PlanNotFound", "The subscription plan for this payment no longer exists."));

        attempt.TryMarkSucceeded(transactionId, request.UtcNow);
        if (attempt.IsRenewal)
        {
            var currentSubscription = await _db.FamilySubscriptions
                .SingleOrDefaultAsync(item => item.Id == attempt.SubscriptionId && item.IsCurrent, cancellationToken);
            if (currentSubscription is null)
                return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                    new Error("Subscriptions.Renewal.NotFound", "The current subscription for renewal was not found."));

            try
            {
                currentSubscription.ApplySuccessfulRenewal(request.UtcNow);
            }
            catch (DomainException exception)
            {
                return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                    new Error("Subscriptions.Renewal.GraceExpired", exception.Message));
            }
        }
        else
        {
            if (await _db.FamilySubscriptions.AnyAsync(
                    item => item.FamilyId == attempt.FamilyId && item.IsCurrent,
                    cancellationToken))
                return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                    new Error("Subscriptions.Payment.CurrentExists", "A current subscription already exists for this family."));

            _db.FamilySubscriptions.Add(FamilySubscription.Create(attempt.FamilyId, plan, request.UtcNow));
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                new Error("Subscriptions.Payment.CurrentExists", "A current subscription already exists for this family."));
        }

        return new ConfirmSubscriptionPaymentResponse(attempt.Id, "Paid");
    }
}
