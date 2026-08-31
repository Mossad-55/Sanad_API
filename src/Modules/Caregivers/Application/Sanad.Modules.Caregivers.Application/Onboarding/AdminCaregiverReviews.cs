using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Onboarding;

public sealed record AdminCaregiverListItem(
    Guid CaregiverId,
    Guid UserId,
    CaregiverType Type,
    CaregiverStatus Status,
    CaregiverAvailability Availability,
    string? ArabicFullName,
    string? EnglishFullName,
    string? PhoneNumber,
    DateTime UpdatedOnUtc);

public sealed record PagedCaregiverList(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<AdminCaregiverListItem> Items);

public sealed record GetCaregiversAdminQuery(
    int Page,
    int PageSize,
    CaregiverStatus? Status,
    CaregiverType? Type)
    : IQuery<PagedCaregiverList>;

public sealed class GetCaregiversAdminQueryValidator
    : AbstractValidator<GetCaregiversAdminQuery>
{
    public GetCaregiversAdminQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetCaregiversAdminQueryHandler
    : IQueryHandler<GetCaregiversAdminQuery, PagedCaregiverList>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetCaregiversAdminQueryHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedCaregiverList>> Handle(
        GetCaregiversAdminQuery request,
        CancellationToken cancellationToken)
    {
        int? statusFilter =
            request.Status.HasValue
                ? (int)request.Status.Value
                : null;

        int? typeFilter =
            request.Type.HasValue
                ? (int)request.Type.Value
                : null;

        IReadOnlyList<AdminCaregiverListItem> items =
            await _dbContext.GetAdminCaregiversAsync(
                request.Page,
                request.PageSize,
                statusFilter,
                typeFilter,
                cancellationToken);

        int totalCount =
            await _dbContext.CountAdminCaregiversAsync(
                statusFilter,
                typeFilter,
                cancellationToken);

        return new PagedCaregiverList(
            request.Page,
            request.PageSize,
            totalCount,
            items);
    }
}

// ----------------------------- Admin detail ---------------------------

public sealed record GetCaregiverAdminDetailQuery(
    CaregiverId CaregiverId)
    : IQuery<CaregiverProfileResponse>;

public sealed class GetCaregiverAdminDetailQueryHandler
    : IQueryHandler<GetCaregiverAdminDetailQuery, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetCaregiverAdminDetailQueryHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        GetCaregiverAdminDetailQuery request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .AsNoTracking()
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.Id == request.CaregiverId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.CaregiverNotFound;
        }

        return caregiver.ToProfileResponse();
    }
}

// ----------------------------- Admin actions --------------------------

public sealed record ApproveCaregiverCommand(
    CaregiverId CaregiverId,
    DateOnly CurrentDate,
    DateTime UtcNow)
    : ICommand;

public sealed class ApproveCaregiverCommandHandler
    : ICommandHandler<ApproveCaregiverCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public ApproveCaregiverCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        ApproveCaregiverCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.Id == request.CaregiverId,
                    cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(
                OnboardingErrors.CaregiverNotFound);
        }

        try
        {
            caregiver.Approve(
                request.UtcNow,
                request.CurrentDate);
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.InvalidState);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed record RejectCaregiverApplicationCommand(
    CaregiverId CaregiverId,
    string Reason,
    DateTime UtcNow)
    : ICommand;

public sealed class RejectCaregiverApplicationCommandValidator
    : AbstractValidator<RejectCaregiverApplicationCommand>
{
    public RejectCaregiverApplicationCommandValidator()
    {
        RuleFor(c => c.CaregiverId).NotEqual(CaregiverId.Empty);
        RuleFor(c => c.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}

public sealed class RejectCaregiverApplicationCommandHandler
    : ICommandHandler<RejectCaregiverApplicationCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public RejectCaregiverApplicationCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        RejectCaregiverApplicationCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.Id == request.CaregiverId,
                    cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(
                OnboardingErrors.CaregiverNotFound);
        }

        try
        {
            caregiver.RejectApplication(
                request.Reason.Trim(),
                request.UtcNow);
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.InvalidState);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed record RequestCaregiverCorrectionCommand(
    CaregiverId CaregiverId,
    string Reason,
    DateTime UtcNow)
    : ICommand;

public sealed class RequestCaregiverCorrectionCommandValidator
    : AbstractValidator<RequestCaregiverCorrectionCommand>
{
    public RequestCaregiverCorrectionCommandValidator()
    {
        RuleFor(c => c.CaregiverId).NotEqual(CaregiverId.Empty);
        RuleFor(c => c.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}

public sealed class RequestCaregiverCorrectionCommandHandler
    : ICommandHandler<RequestCaregiverCorrectionCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public RequestCaregiverCorrectionCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        RequestCaregiverCorrectionCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.Id == request.CaregiverId,
                    cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(
                OnboardingErrors.CaregiverNotFound);
        }

        try
        {
            caregiver.RequestCorrection(
                request.Reason.Trim(),
                request.UtcNow);
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.InvalidState);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed record SuspendCaregiverCommand(
    CaregiverId CaregiverId,
    string Reason,
    DateTime UtcNow)
    : ICommand;

public sealed class SuspendCaregiverCommandValidator
    : AbstractValidator<SuspendCaregiverCommand>
{
    public SuspendCaregiverCommandValidator()
    {
        RuleFor(c => c.CaregiverId).NotEqual(CaregiverId.Empty);
        RuleFor(c => c.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}

public sealed class SuspendCaregiverCommandHandler
    : ICommandHandler<SuspendCaregiverCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public SuspendCaregiverCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        SuspendCaregiverCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.Id == request.CaregiverId,
                    cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(
                OnboardingErrors.CaregiverNotFound);
        }

        try
        {
            caregiver.Suspend(
                request.Reason.Trim(),
                request.UtcNow);
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.InvalidState);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed record ReactivateCaregiverCommand(
    CaregiverId CaregiverId,
    DateOnly CurrentDate,
    DateTime UtcNow)
    : ICommand;

public sealed class ReactivateCaregiverCommandHandler
    : ICommandHandler<ReactivateCaregiverCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public ReactivateCaregiverCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        ReactivateCaregiverCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.Id == request.CaregiverId,
                    cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(
                OnboardingErrors.CaregiverNotFound);
        }

        try
        {
            caregiver.Reactivate(
                request.UtcNow,
                request.CurrentDate);
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.InvalidState);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}