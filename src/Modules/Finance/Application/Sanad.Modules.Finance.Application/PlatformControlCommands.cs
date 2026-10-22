using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Application;

/// <summary>
/// The mutable platform control values persisted in the single control record.
/// This shape never crosses the public status boundary; the public status DTO
/// is deliberately narrower.
/// </summary>
public sealed record PlatformControlValues(
    PlatformState State,
    string? MaintenanceMessageArabic,
    string? MaintenanceMessageEnglish,
    bool CardEnabled,
    bool WalletEnabled);

/// <summary>
/// Persisted control state plus its optimistic concurrency version.
/// </summary>
public sealed record PlatformControlSnapshot(
    Guid Id,
    PlatformControlValues Values,
    int Version,
    DateTime UpdatedOnUtc);

public interface IPlatformControlReader
{
    /// <summary>
    /// Returns the persisted control state, or <c>null</c> when no record
    /// exists. Callers must treat a null or failed read as unavailable and
    /// fail closed; they must never assume Operational.
    /// </summary>
    Task<PlatformControlSnapshot?> GetAsync(CancellationToken cancellationToken);
}

public interface IPlatformControlWriter
{
    Task<Result<PlatformControlSnapshot>> UpdateAsync(
        int expectedVersion,
        PlatformControlValues values,
        Guid actorUserId,
        string? internalReason,
        CancellationToken cancellationToken);
}

/// <summary>
/// Truth about whether each supported payment method's existing provider
/// integration is configured. Availability is only advertised when the method
/// is both enabled in the control record and configured here.
/// </summary>
public interface IPaymentMethodConfiguration
{
    bool CardConfigured { get; }
    bool WalletConfigured { get; }

    /// <summary>
    /// Path-specific technical truth for the single branch that does not use the
    /// generic Card integration: a Card initial subscription-enrollment intent,
    /// which the gateway client sends through the dedicated 3DS enrollment
    /// integration whenever the resolved plan carries a provider subscription
    /// plan id (matching <c>PaymobClient.cardEnrollment</c>).
    /// This never widens or narrows the advertised method list and never changes
    /// the generic Card policy: <see cref="CardConfigured"/> remains the truth
    /// for regular Card flows and for the public status response.
    /// </summary>
    bool CardSubscriptionEnrollmentConfigured { get; }
}

public static class PlatformControlErrors
{
    /// <summary>Generic fail-closed result when the control record cannot be read.</summary>
    public static readonly Error Unavailable =
        new("Platform.ControlStateUnavailable", "Platform control state is unavailable.");

    public static readonly Error VersionConflict =
        new("Platform.ControlVersionConflict", "The platform control state changed concurrently.");

    public static readonly Error Invalid =
        new("Platform.Control.Invalid", "The platform control update is invalid.");

    public static readonly Error Maintenance =
        new("Platform.Maintenance", "New booking and payment operations are temporarily unavailable for maintenance.");

    public static readonly Error PaymentMethodUnavailable =
        new("Platform.PaymentMethodUnavailable", "The selected payment method is not available.");
}

public sealed record PlatformControlResponse(
    int Version,
    PlatformState State,
    string? MaintenanceMessageArabic,
    string? MaintenanceMessageEnglish,
    bool CardEnabled,
    bool WalletEnabled,
    DateTime UpdatedOnUtc);

public sealed record PublicPlatformStatusResponse(
    PlatformState State,
    string? MaintenanceMessageArabic,
    string? MaintenanceMessageEnglish,
    IReadOnlyList<PlatformPaymentMethod> AvailablePaymentMethods,
    bool SubscriptionCardEnrollmentAvailable);

public sealed record GetPlatformControlQuery : IQuery<PlatformControlResponse?>;

public sealed record GetPublicPlatformStatusQuery : IQuery<PublicPlatformStatusResponse>;

public sealed record UpdatePlatformControlCommand(
    int ExpectedVersion,
    PlatformState State,
    string? MaintenanceMessageArabic,
    string? MaintenanceMessageEnglish,
    bool CardEnabled,
    bool WalletEnabled,
    Guid ActorUserId,
    string? InternalReason = null) : ICommand<PlatformControlResponse>;

public static class PlatformControlResponses
{
    public static PlatformControlResponse Map(PlatformControlSnapshot snapshot) =>
        new(
            snapshot.Version,
            snapshot.Values.State,
            snapshot.Values.MaintenanceMessageArabic,
            snapshot.Values.MaintenanceMessageEnglish,
            snapshot.Values.CardEnabled,
            snapshot.Values.WalletEnabled,
            snapshot.UpdatedOnUtc);
}

/// <summary>
/// Shared authoritative evaluator for payment-method availability. Both the
/// public status (discovery) and the payment-intent gates (enforcement) use
/// these methods, so a method can never be advertised as available while the
/// corresponding intent is rejected for the same control/configuration state.
/// </summary>
public static class PaymentAvailability
{
    public static bool IsCardAvailable(PlatformControlValues values, IPaymentMethodConfiguration methods) =>
        values.CardEnabled && methods.CardConfigured;

    public static bool IsWalletAvailable(PlatformControlValues values, IPaymentMethodConfiguration methods) =>
        values.WalletEnabled && methods.WalletConfigured;

    /// <summary>
    /// Truth for the Card branch that performs an initial subscription
    /// enrollment through the dedicated 3DS integration when the resolved
    /// plan carries a provider subscription plan id. This is the generic Card
    /// truth plus the enrollment integration; it never widens the generic
    /// Card policy and never narrows it for plans without a provider
    /// subscription plan id.
    /// </summary>
    public static bool IsSubscriptionCardEnrollmentAvailable(PlatformControlValues values, IPaymentMethodConfiguration methods) =>
        IsCardAvailable(values, methods) && methods.CardSubscriptionEnrollmentConfigured;

    /// <summary>
    /// Config-level truth used by the plan-dependent enforcement branch after
    /// the generic Card gate has already passed for the same control snapshot.
    /// </summary>
    public static bool IsSubscriptionCardEnrollmentConfigured(IPaymentMethodConfiguration methods) =>
        methods.CardSubscriptionEnrollmentConfigured;
}

public sealed class GetPlatformControlQueryHandler(IPlatformControlReader reader)
    : IQueryHandler<GetPlatformControlQuery, PlatformControlResponse?>
{
    public async Task<Result<PlatformControlResponse?>> Handle(GetPlatformControlQuery request, CancellationToken ct)
    {
        PlatformControlSnapshot? snapshot;
        try
        {
            snapshot = await reader.GetAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<PlatformControlResponse?>.Failure(PlatformControlErrors.Unavailable);
        }

        return snapshot is null
            ? Result<PlatformControlResponse?>.Failure(PlatformControlErrors.Unavailable)
            : Result<PlatformControlResponse?>.Success(PlatformControlResponses.Map(snapshot));
    }
}

public sealed class GetPublicPlatformStatusQueryHandler(
    IPlatformControlReader reader,
    IPaymentMethodConfiguration paymentMethods)
    : IQueryHandler<GetPublicPlatformStatusQuery, PublicPlatformStatusResponse>
{
    public async Task<Result<PublicPlatformStatusResponse>> Handle(GetPublicPlatformStatusQuery request, CancellationToken ct)
    {
        PlatformControlSnapshot? snapshot;
        try
        {
            snapshot = await reader.GetAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<PublicPlatformStatusResponse>.Failure(PlatformControlErrors.Unavailable);
        }

        if (snapshot is null)
            return Result<PublicPlatformStatusResponse>.Failure(PlatformControlErrors.Unavailable);

        var available = new List<PlatformPaymentMethod>(2);
        if (PaymentAvailability.IsCardAvailable(snapshot.Values, paymentMethods))
            available.Add(PlatformPaymentMethod.Card);
        if (PaymentAvailability.IsWalletAvailable(snapshot.Values, paymentMethods))
            available.Add(PlatformPaymentMethod.Wallet);

        return Result<PublicPlatformStatusResponse>.Success(
            new(
                snapshot.Values.State,
                snapshot.Values.MaintenanceMessageArabic,
                snapshot.Values.MaintenanceMessageEnglish,
                available,
                PaymentAvailability.IsSubscriptionCardEnrollmentAvailable(snapshot.Values, paymentMethods)));
    }
}

public sealed class UpdatePlatformControlCommandHandler(IPlatformControlWriter writer)
    : ICommandHandler<UpdatePlatformControlCommand, PlatformControlResponse>
{
    public async Task<Result<PlatformControlResponse>> Handle(UpdatePlatformControlCommand request, CancellationToken ct)
    {
        try
        {
            var result = await writer.UpdateAsync(
                request.ExpectedVersion,
                new PlatformControlValues(
                    request.State,
                    request.MaintenanceMessageArabic,
                    request.MaintenanceMessageEnglish,
                    request.CardEnabled,
                    request.WalletEnabled),
                request.ActorUserId,
                request.InternalReason,
                ct);

            return result.IsSuccess
                ? Result<PlatformControlResponse>.Success(PlatformControlResponses.Map(result.Value))
                : Result<PlatformControlResponse>.Failure(result.Error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<PlatformControlResponse>.Failure(PlatformControlErrors.Unavailable);
        }
    }
}
