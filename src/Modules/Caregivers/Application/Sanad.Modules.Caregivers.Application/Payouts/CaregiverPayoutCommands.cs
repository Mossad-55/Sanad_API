using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.PayoutAccounts;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Caregivers.Application.Payouts;

public static class CaregiverPayoutErrors
{
    public static readonly Error NotFound =
        new(
            "Caregivers.Payouts.NotFound",
            "The caregiver payout was not found.");

    public static readonly Error Ineligible =
        new(
            "Caregivers.Payouts.Ineligible",
            "The booking is not eligible for payout.");

    public static readonly Error Conflict =
        new(
            "Caregivers.Payouts.Conflict",
            "A paid payout already exists for this booking.");

    public static readonly Error InvalidState =
        new(
            "Caregivers.Payouts.InvalidState",
            "The payout is not in a valid state for this operation.");
}

public sealed record CaregiverPayoutResponse(
    Guid PayoutId,
    Guid BookingId,
    Guid CaregiverId,
    decimal GrossAmount,
    decimal PlatformFeeAmount,
    decimal NetAmount,
    string Currency,
    string Status,
    string TransferReference,
    string Evidence,
    string? FailureReason,
    DateTime RecordedOnUtc,
    DateTime? PaidOnUtc,
    DateTime? FailedOnUtc,
    Guid RecordedBy,
    int PolicyVersion,
    string BankCode,
    string MaskedIban);

internal static class CaregiverPayoutMappings
{
    public static CaregiverPayoutResponse ToResponse(
        this CaregiverPayout payout)
    {
        return new CaregiverPayoutResponse(
            payout.Id.Value,
            payout.BookingId.Value,
            payout.CaregiverId.Value,
            payout.GrossAmount,
            payout.PlatformFeeAmount,
            payout.NetAmount,
            payout.Currency,
            payout.Status.ToString(),
            payout.TransferReference,
            payout.Evidence,
            payout.FailureReason,
            payout.RecordedOnUtc,
            payout.Status == PayoutStatus.Paid ? payout.RecordedOnUtc : null,
            payout.FailedOnUtc,
            payout.RecordedBy.Value,
            payout.PolicyVersion,
            payout.BankCode,
            payout.MaskedIban);
    }
}

public sealed record GetCaregiverPayoutsQuery(
    PayoutStatus? Status = null,
    Guid? CaregiverId = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<CaregiverPayoutResponse>>;

public sealed class GetCaregiverPayoutsQueryHandler(
    ICaregiversDbContext dbContext) : IQueryHandler<
    GetCaregiverPayoutsQuery,
    PagedResult<CaregiverPayoutResponse>>
{
    public async Task<Result<PagedResult<CaregiverPayoutResponse>>> Handle(
        GetCaregiverPayoutsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        var query = dbContext.Payouts.AsNoTracking().AsQueryable();

        if (request.Status.HasValue)
        {
            query = query.Where(p => p.Status == request.Status.Value);
        }

        if (request.CaregiverId.HasValue)
        {
            var caregiverId = new CaregiverId(request.CaregiverId.Value);
            query = query.Where(p => p.CaregiverId == caregiverId);
        }

        int totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(p => p.RecordedOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return Result<PagedResult<CaregiverPayoutResponse>>.Success(
            new PagedResult<CaregiverPayoutResponse>(
                items.Select(p => p.ToResponse()).ToList(),
                page,
                pageSize,
                totalCount,
                totalPages));
    }
}

public sealed record GetCaregiverPayoutQuery(
    Guid PayoutId) : IQuery<CaregiverPayoutResponse>;

public sealed class GetCaregiverPayoutQueryHandler(
    ICaregiversDbContext dbContext) : IQueryHandler<
    GetCaregiverPayoutQuery,
    CaregiverPayoutResponse>
{
    public async Task<Result<CaregiverPayoutResponse>> Handle(
        GetCaregiverPayoutQuery request,
        CancellationToken cancellationToken)
    {
        var payout = await dbContext.Payouts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.Id == new CaregiverPayoutId(request.PayoutId),
                cancellationToken);

        if (payout is null)
        {
            return Result<CaregiverPayoutResponse>.Failure(
                CaregiverPayoutErrors.NotFound);
        }

        return Result<CaregiverPayoutResponse>.Success(
            payout.ToResponse());
    }
}

public sealed record RecordCaregiverPayoutCommand(
    Guid BookingId,
    UserId ActorUserId,
    string TransferReference,
    string Evidence,
    string Reason,
    DateTime UtcNow) : ICommand<CaregiverPayoutResponse>;

public sealed class RecordCaregiverPayoutCommandValidator :
    AbstractValidator<RecordCaregiverPayoutCommand>
{
    public RecordCaregiverPayoutCommandValidator()
    {
        RuleFor(command => command.BookingId)
            .NotEqual(Guid.Empty);

        RuleFor(command => command.TransferReference)
            .NotEmpty()
            .MaximumLength(CaregiverPayout.MaximumReferenceLength);

        RuleFor(command => command.Evidence)
            .NotEmpty()
            .MaximumLength(CaregiverPayout.MaximumEvidenceLength);

        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(CaregiverPayout.MaximumReasonLength);
    }
}

public sealed class RecordCaregiverPayoutCommandHandler(
    ICaregiversDbContext dbContext,
    IFamiliesDbContext familiesDb,
    ICaregiverPayoutPolicyReader policyReader) : ICommandHandler<
    RecordCaregiverPayoutCommand,
    CaregiverPayoutResponse>
{
    public async Task<Result<CaregiverPayoutResponse>> Handle(
        RecordCaregiverPayoutCommand request,
        CancellationToken cancellationToken)
    {
        if (request.UtcNow.Kind != DateTimeKind.Utc)
        {
            return Result<CaregiverPayoutResponse>.Failure(
                CaregiverPayoutErrors.InvalidState);
        }

        var booking = await familiesDb.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                b => b.Id == new BookingId(request.BookingId),
                cancellationToken);

        if (booking is null ||
            booking.Status != BookingStatus.Completed ||
            booking.CompletedOnUtc is null ||
            booking.PaidOnUtc is null)
        {
            return Result<CaregiverPayoutResponse>.Failure(
                CaregiverPayoutErrors.Ineligible);
        }

        var rates = await policyReader.GetEffectiveAsync(
            request.UtcNow,
            cancellationToken);

        if (rates is null)
        {
            return Result<CaregiverPayoutResponse>.Failure(
                CaregiverPayoutPolicyErrors.Missing);
        }

        var eligibility = PayoutEligibility.Evaluate(
            booking.CompletedOnUtc,
            booking.PaidOnUtc,
            rates.PayoutDelayHours,
            request.UtcNow);

        if (!eligibility.IsEligible)
        {
            return Result<CaregiverPayoutResponse>.Failure(
                CaregiverPayoutErrors.Ineligible);
        }

        var account = await dbContext.PayoutAccounts
            .AsNoTracking()
            .SingleOrDefaultAsync(
                a => a.CaregiverId == booking.CaregiverId,
                cancellationToken);

        if (account is null)
        {
            return Result<CaregiverPayoutResponse>.Failure(
                PayoutAccountErrors.NotFound);
        }

        bool alreadyPaid = await dbContext.Payouts.AnyAsync(
            p => p.BookingId == booking.Id &&
                 p.Status == PayoutStatus.Paid,
            cancellationToken);

        if (alreadyPaid)
        {
            return Result<CaregiverPayoutResponse>.Failure(
                CaregiverPayoutErrors.Conflict);
        }

        CaregiverPayout payout;
        try
        {
            payout = CaregiverPayout.Record(
                booking.Id,
                booking.CaregiverId,
                booking.PriceSnapshot.BaseCaregiverFee,
                booking.PriceSnapshot.PlatformFeeAmount,
                booking.PriceSnapshot.Currency,
                rates.Version,
                booking.PriceSnapshot.PlatformChargeRuleVersion,
                account.BankCode,
                account.MaskedIban(),
                request.TransferReference,
                request.Evidence,
                request.Reason,
                request.ActorUserId,
                request.UtcNow);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException)
        {
            return Result<CaregiverPayoutResponse>.Failure(
                CaregiverPayoutErrors.InvalidState);
        }

        dbContext.Payouts.Add(payout);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains(
            "ux_caregiver_payouts_booking_paid",
            StringComparison.Ordinal))
        {
            return Result<CaregiverPayoutResponse>.Failure(
                CaregiverPayoutErrors.Conflict);
        }

        return Result<CaregiverPayoutResponse>.Success(
            payout.ToResponse());
    }
}

public sealed record MarkCaregiverPayoutFailedCommand(
    Guid PayoutId,
    UserId ActorUserId,
    string Reason,
    DateTime UtcNow) : ICommand<CaregiverPayoutResponse>;

public sealed class MarkCaregiverPayoutFailedCommandValidator :
    AbstractValidator<MarkCaregiverPayoutFailedCommand>
{
    public MarkCaregiverPayoutFailedCommandValidator()
    {
        RuleFor(command => command.PayoutId)
            .NotEqual(Guid.Empty);

        RuleFor(command => command.Reason)
            .NotEmpty()
            .MaximumLength(CaregiverPayout.MaximumReasonLength);
    }
}

public sealed class MarkCaregiverPayoutFailedCommandHandler(
    ICaregiversDbContext dbContext) : ICommandHandler<
    MarkCaregiverPayoutFailedCommand,
    CaregiverPayoutResponse>
{
    public async Task<Result<CaregiverPayoutResponse>> Handle(
        MarkCaregiverPayoutFailedCommand request,
        CancellationToken cancellationToken)
    {
        var payout = await dbContext.Payouts
            .SingleOrDefaultAsync(
                p => p.Id == new CaregiverPayoutId(request.PayoutId),
                cancellationToken);

        if (payout is null)
        {
            return Result<CaregiverPayoutResponse>.Failure(
                CaregiverPayoutErrors.NotFound);
        }

        try
        {
            payout.MarkFailed(
                request.Reason,
                request.ActorUserId,
                request.UtcNow);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException)
        {
            return Result<CaregiverPayoutResponse>.Failure(
                CaregiverPayoutErrors.InvalidState);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<CaregiverPayoutResponse>.Success(
            payout.ToResponse());
    }
}
