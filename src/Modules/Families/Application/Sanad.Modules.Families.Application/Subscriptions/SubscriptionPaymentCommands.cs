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
    private readonly IPaymobClient _paymobClient;

    public ConfirmSubscriptionPaymentCommandHandler(IFamiliesDbContext db, IPaymobClient paymobClient) { _db = db; _paymobClient = paymobClient; }

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

        if (attempt.IsPlanChange)
        {
            var currentSubscription = await _db.FamilySubscriptions.SingleOrDefaultAsync(item => item.Id == attempt.SubscriptionId && item.IsCurrent, cancellationToken);
            if (currentSubscription is null) return Result<ConfirmSubscriptionPaymentResponse>.Failure(new Error("Subscriptions.PlanChange.NotFound", "The current subscription was not found."));
            if (currentSubscription.CancellationRequestedOnUtc is not null || currentSubscription.IsWithinRenewalGrace(request.UtcNow))
                return Result<ConfirmSubscriptionPaymentResponse>.Failure(new Error("Subscriptions.PlanChange.Unavailable", "Plan changes are unavailable for the current subscription state."));
            if (attempt.Method == SubscriptionPaymentMethod.Card)
            {
                if (string.IsNullOrWhiteSpace(currentSubscription.PaymobSubscriptionId))
                    return Result<ConfirmSubscriptionPaymentResponse>.Failure(new Error("Paymob.SubscriptionNotFound", "No provider subscription is available to update."));
                decimal futureGross = decimal.Round(plan.Price * (1m + attempt.TaxRatePercentage / 100m), 2, MidpointRounding.ToEven);
                var update = await _paymobClient.UpdateSubscriptionAmountAsync(currentSubscription.PaymobSubscriptionId, futureGross, cancellationToken);
                if (!update.IsSuccess) return Result<ConfirmSubscriptionPaymentResponse>.Failure(update.Error);
            }
            currentSubscription.ApplyImmediatePlanChange(plan, attempt.BasePrice, attempt.TaxRatePercentage);
            attempt.TryMarkSucceeded(transactionId, request.UtcNow);
        }
        else if (attempt.IsRenewal)
        {
            var currentSubscription = await _db.FamilySubscriptions
                .SingleOrDefaultAsync(item => item.Id == attempt.SubscriptionId && item.IsCurrent, cancellationToken);
            if (currentSubscription is null)
                return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                    new Error("Subscriptions.Renewal.NotFound", "The current subscription for renewal was not found."));

            try
            {
                currentSubscription.ApplySuccessfulRenewal(request.UtcNow);
                currentSubscription.SetCurrentPeriodSettlement(attempt.TotalPayable, attempt.TaxRatePercentage);
                attempt.TryMarkSucceeded(transactionId, request.UtcNow);
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

            var createdSubscription = FamilySubscription.Create(attempt.FamilyId, plan, request.UtcNow);
            createdSubscription.SetCurrentPeriodSettlement(attempt.TotalPayable, attempt.TaxRatePercentage);
            attempt.LinkSubscription(createdSubscription.Id);
            if (!string.IsNullOrWhiteSpace(attempt.PaymobSubscriptionId))
            {
                createdSubscription.AssociatePaymobSubscription(
                    attempt.PaymobSubscriptionId,
                    state: null,
                    nextBillingOnUtc: null);
            }

            _db.FamilySubscriptions.Add(createdSubscription);
            attempt.TryMarkSucceeded(transactionId, request.UtcNow);
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

public sealed record HandlePaymobSubscriptionCallbackCommand(
    string TriggerType,
    string ProviderSubscriptionId,
    string? InitialTransactionId,
    long? AmountCents,
    string? State,
    DateTime? NextBillingOnUtc,
    DateTime UtcNow,
    string? PaymobRequestId = null) : ICommand<ConfirmSubscriptionPaymentResponse>;

public sealed class HandlePaymobSubscriptionCallbackCommandHandler
    : ICommandHandler<HandlePaymobSubscriptionCallbackCommand, ConfirmSubscriptionPaymentResponse>
{
    private readonly IFamiliesDbContext _db;

    public HandlePaymobSubscriptionCallbackCommandHandler(IFamiliesDbContext db) => _db = db;

    public async Task<Result<ConfirmSubscriptionPaymentResponse>> Handle(
        HandlePaymobSubscriptionCallbackCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderSubscriptionId))
            return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                new Error("Subscriptions.Payment.NotFound", "No subscription matches this provider subscription."));

        string callbackKey = BuildCallbackKey(request);
        bool isCreated = IsTrigger(request.TriggerType, "CREATED", "Subscription Created");
        bool isSuccess = IsTrigger(request.TriggerType, "Successful Transaction");
        bool isFailed = IsTrigger(request.TriggerType, "Failed Transaction");
        bool isOverdue = IsTrigger(request.TriggerType, "Failed Overdue Transaction");

        if (isCreated)
        {
            if (string.IsNullOrWhiteSpace(request.InitialTransactionId))
                return new ConfirmSubscriptionPaymentResponse(Guid.Empty, "Ignored");

            string providerSubscriptionId = request.ProviderSubscriptionId.Trim();

            var initialAttempt = await _db.SubscriptionPaymentAttempts
                .SingleOrDefaultAsync(
                    item => item.PaymobTransactionId == request.InitialTransactionId
                        || item.PaymobInitialTransactionId == request.InitialTransactionId,
                    cancellationToken);
            if (initialAttempt is null)
                return new ConfirmSubscriptionPaymentResponse(Guid.Empty, "Ignored");

            if (!string.IsNullOrWhiteSpace(initialAttempt.PaymobSubscriptionId)
                && !string.Equals(initialAttempt.PaymobSubscriptionId, providerSubscriptionId, StringComparison.Ordinal))
                return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                    new Error("Subscriptions.Payment.ProviderIdentityConflict", "The provider subscription identity conflicts with the initial payment."));

            if (await _db.SubscriptionPaymentAttempts.AnyAsync(
                    item => item.PaymobSubscriptionId == providerSubscriptionId
                        && item.Id != initialAttempt.Id,
                    cancellationToken))
                return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                    new Error("Subscriptions.Payment.ProviderIdentityConflict", "The provider subscription identity is already associated with another payment attempt."));

            FamilySubscription? createdSubscription = null;
            if (initialAttempt.SubscriptionId is Guid subscriptionId)
            {
                createdSubscription = await _db.FamilySubscriptions
                    .SingleOrDefaultAsync(item => item.Id == subscriptionId, cancellationToken);
                if (createdSubscription is not null
                    && !string.IsNullOrWhiteSpace(createdSubscription.PaymobSubscriptionId)
                    && !string.Equals(createdSubscription.PaymobSubscriptionId, providerSubscriptionId, StringComparison.Ordinal))
                    return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                        new Error("Subscriptions.Payment.ProviderIdentityConflict", "The provider subscription identity conflicts with the subscription."));
            }

            var registeredIdentity = await _db.PaymobSubscriptionIdentities
                .SingleOrDefaultAsync(item => item.ProviderSubscriptionId == providerSubscriptionId, cancellationToken);
            if (registeredIdentity is not null)
            {
                if (registeredIdentity.PaymentAttemptId != initialAttempt.Id
                    || registeredIdentity.FamilySubscriptionId != initialAttempt.SubscriptionId)
                    return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                        new Error("Subscriptions.Payment.ProviderIdentityConflict", "The provider subscription identity is already owned by another payment target."));

                return new ConfirmSubscriptionPaymentResponse(initialAttempt.Id, "AlreadyProcessed");
            }

            if (await _db.PaymobSubscriptionCallbacks.AnyAsync(item => item.CallbackKey == callbackKey, cancellationToken))
                return new ConfirmSubscriptionPaymentResponse(initialAttempt.Id, "AlreadyProcessed");

            if (await _db.FamilySubscriptions.AnyAsync(
                    item => item.PaymobSubscriptionId == providerSubscriptionId
                        && (initialAttempt.SubscriptionId == null || item.Id != initialAttempt.SubscriptionId),
                    cancellationToken))
                return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                    new Error("Subscriptions.Payment.ProviderIdentityConflict", "The provider subscription identity is already associated with another subscription."));

            var callback = PaymobSubscriptionCallback.Create(
                callbackKey, request.PaymobRequestId, request.ProviderSubscriptionId, request.TriggerType, request.UtcNow);
            // This is the authoritative provider-identity reservation boundary. The registry's
            // unique constraint arbitrates competing targets at SaveChangesAsync; all callback
            // state changes below are committed in that same database transaction.
            _db.ReservePaymobSubscriptionIdentity(
                PaymobSubscriptionIdentity.Create(
                    providerSubscriptionId,
                    initialAttempt.Id,
                    initialAttempt.SubscriptionId));

            if (string.IsNullOrWhiteSpace(initialAttempt.PaymobSubscriptionId))
                initialAttempt.RecordPaymobSubscription(
                    providerSubscriptionId,
                    request.InitialTransactionId);

            if (createdSubscription is not null
                && string.IsNullOrWhiteSpace(createdSubscription.PaymobSubscriptionId))
            {
                createdSubscription.AssociatePaymobSubscription(
                    providerSubscriptionId,
                    request.State,
                    request.NextBillingOnUtc,
                    callbackKey);
            }

            _db.PaymobSubscriptionCallbacks.Add(callback);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                    new Error("Subscriptions.Callback.ConcurrencyConflict", "The subscription callback conflicted with another update."));
            }
            catch (DbUpdateException exception) when (IsCallbackUniqueViolation(exception))
            {
                return new ConfirmSubscriptionPaymentResponse(initialAttempt.Id, "AlreadyProcessed");
            }
            catch (DbUpdateException exception) when (IsProviderIdentityUniqueViolation(exception))
            {
                var racedIdentity = await _db.PaymobSubscriptionIdentities
                    .AsNoTracking()
                    .SingleOrDefaultAsync(item => item.ProviderSubscriptionId == providerSubscriptionId, cancellationToken);
                if (racedIdentity is not null
                    && racedIdentity.PaymentAttemptId == initialAttempt.Id
                    && racedIdentity.FamilySubscriptionId == initialAttempt.SubscriptionId)
                    return new ConfirmSubscriptionPaymentResponse(initialAttempt.Id, "AlreadyProcessed");

                return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                    new Error("Subscriptions.Payment.ProviderIdentityConflict", "The provider subscription identity is already owned by another payment target."));
            }
            return new ConfirmSubscriptionPaymentResponse(initialAttempt.Id, "Created");
        }

        if (!isSuccess && !isFailed && !isOverdue)
            return new ConfirmSubscriptionPaymentResponse(Guid.Empty, "Ignored");

        if (string.IsNullOrWhiteSpace(request.PaymobRequestId))
            return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                new Error(
                    "Subscriptions.Callback.ProviderEventIdRequired",
                    "A Paymob provider event identity is required for renewal callbacks."));

        if (request.AmountCents is null)
            return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                new Error("Paymob.AmountMismatch", "Webhook amount is required for a subscription transaction."));

        var currentSubscription = await _db.FamilySubscriptions
            .SingleOrDefaultAsync(
                item => item.IsCurrent && item.PaymobSubscriptionId == request.ProviderSubscriptionId,
                cancellationToken);
        if (currentSubscription is null)
            return new ConfirmSubscriptionPaymentResponse(Guid.Empty, "Ignored");

        if (await _db.PaymobSubscriptionCallbacks.AnyAsync(
                item => item.ProviderSubscriptionId == request.ProviderSubscriptionId
                    && item.ReceivedOnUtc > request.UtcNow,
                cancellationToken))
            return new ConfirmSubscriptionPaymentResponse(currentSubscription.Id, "AlreadyProcessed");

        if (await _db.PaymobSubscriptionCallbacks.AnyAsync(item => item.CallbackKey == callbackKey, cancellationToken)
            || currentSubscription.HasProcessedPaymobCallback(callbackKey))
            return new ConfirmSubscriptionPaymentResponse(currentSubscription.Id, "AlreadyProcessed");

        long expectedCents = (long)decimal.Round(currentSubscription.Price * 100m, 0, MidpointRounding.ToEven);
        if (expectedCents != request.AmountCents.Value)
            return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                new Error("Paymob.AmountMismatch", "Webhook amount does not match the recorded subscription."));

        if (isSuccess)
        {
            try
            {
                currentSubscription.ApplySuccessfulRenewal(request.UtcNow);
            }
            catch (DomainException exception)
            {
                var graceExpiredCallback = PaymobSubscriptionCallback.Create(
                    callbackKey, request.PaymobRequestId, request.ProviderSubscriptionId, request.TriggerType, request.UtcNow);
                _db.PaymobSubscriptionCallbacks.Add(graceExpiredCallback);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                        new Error("Subscriptions.Callback.ConcurrencyConflict", "The subscription callback conflicted with another update."));
                }
                catch (DbUpdateException callbackException) when (IsCallbackUniqueViolation(callbackException))
                {
                    return new ConfirmSubscriptionPaymentResponse(currentSubscription.Id, "AlreadyProcessed");
                }

                return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                    new Error("Subscriptions.Renewal.GraceExpired", exception.Message));
            }
        }
        else if (isFailed)
        {
            currentSubscription.BeginRenewalGrace(request.UtcNow);
        }

        var renewalCallback = PaymobSubscriptionCallback.Create(
            callbackKey, request.PaymobRequestId, request.ProviderSubscriptionId, request.TriggerType, request.UtcNow);
        currentSubscription.AssociatePaymobSubscription(
            request.ProviderSubscriptionId,
            request.State,
            request.NextBillingOnUtc,
            callbackKey);
        _db.PaymobSubscriptionCallbacks.Add(renewalCallback);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<ConfirmSubscriptionPaymentResponse>.Failure(
                new Error("Subscriptions.Callback.ConcurrencyConflict", "The subscription callback conflicted with another update."));
        }
        catch (DbUpdateException exception) when (IsCallbackUniqueViolation(exception))
        {
            return new ConfirmSubscriptionPaymentResponse(currentSubscription.Id, "AlreadyProcessed");
        }
        return new ConfirmSubscriptionPaymentResponse(currentSubscription.Id, isOverdue ? "Overdue" : isFailed ? "Failed" : "Paid");
    }

    private static bool IsTrigger(string actual, params string[] expected) =>
        expected.Any(item => string.Equals(actual.Trim(), item, StringComparison.OrdinalIgnoreCase));

    private static string BuildCallbackKey(HandlePaymobSubscriptionCallbackCommand request) =>
        !string.IsNullOrWhiteSpace(request.PaymobRequestId)
            ? $"request:{request.PaymobRequestId.Trim()}"
            : $"fallback:{string.Join("|", request.TriggerType.Trim(), request.ProviderSubscriptionId.Trim(),
            request.InitialTransactionId?.Trim() ?? string.Empty,
            request.AmountCents?.ToString() ?? string.Empty,
            request.NextBillingOnUtc?.ToString("O") ?? string.Empty)}";

    private static bool IsCallbackUniqueViolation(DbUpdateException exception) =>
        exception.ToString().Contains("ux_paymob_subscription_callbacks_", StringComparison.Ordinal);

    private static bool IsProviderIdentityUniqueViolation(DbUpdateException exception) =>
        exception.ToString().Contains("ux_paymob_subscription_identities_provider_subscription_id", StringComparison.Ordinal);
}
