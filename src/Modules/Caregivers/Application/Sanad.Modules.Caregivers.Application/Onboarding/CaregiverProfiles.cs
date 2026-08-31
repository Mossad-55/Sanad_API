using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Onboarding;

// ----------------------------- Read model -----------------------------

public sealed record CaregiverProfileResponse(
    CaregiverId Id,
    UserId UserId,
    CaregiverType Type,
    CaregiverStatus Status,
    CaregiverAvailability Availability,
    string? DetailedAddress,
    string? StatusReason,
    MedicalProfileResponse? MedicalProfile,
    CompanionProfileResponse? CompanionProfile,
    IReadOnlyList<CertificateItemResponse> Certificates,
    IReadOnlyList<ServiceId> ServiceIds,
    IReadOnlyList<LanguageId> LanguageIds,
    IReadOnlyList<AreaId> AreaIds,
    MedicalPricingResponse? MedicalPricing,
    CompanionPricingResponse? CompanionPricing,
    MedicalScheduleResponse? MedicalSchedule,
    CompanionScheduleResponse? CompanionSchedule);

public sealed record MedicalPricingResponse(
    decimal HomeVisitPrice,
    decimal EightHourShiftPrice,
    decimal TwelveHourShiftPrice,
    decimal TwentyFourHourShiftPrice);

public sealed record CompanionPricingResponse(
    decimal HourlyPrice,
    decimal EightHourDayPrice,
    decimal OvernightPrice);

public sealed record MedicalScheduleResponse(
    IReadOnlyList<MedicalShiftItemResponse> Shifts,
    IReadOnlyList<MedicalHomeVisitWindowItemResponse> HomeVisitWindows);

public sealed record MedicalShiftItemResponse(
    DayOfWeek DayOfWeek,
    MedicalShiftType ShiftType);

public sealed record MedicalHomeVisitWindowItemResponse(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed record CompanionScheduleResponse(
    IReadOnlyList<CompanionAvailabilityWindowItemResponse> Windows);

public sealed record CompanionAvailabilityWindowItemResponse(
    CompanionBookingType BookingType,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed record MedicalProfileResponse(
    ProfessionalTitleId ProfessionalTitleId,
    int YearsOfExperience,
    SpecializationId SpecializationId,
    AcademicDegreeId AcademicDegreeId,
    string? CurrentWorkplace,
    string? Biography);

public sealed record CompanionProfileResponse(
    int YearsOfExperience,
    SpecializationId SpecializationId,
    string? Biography);

public sealed record CertificateItemResponse(
    CaregiverCertificateId Id,
    CaregiverCertificateType Type,
    DateOnly? ExpiryDate,
    CertificateVerificationStatus VerificationStatus,
    string? ReviewReason);

internal static class CaregiverProfileMappings
{
    public static CaregiverProfileResponse ToProfileResponse(
        this Caregiver caregiver) =>
        new(
            caregiver.Id,
            caregiver.UserId,
            caregiver.Type,
            caregiver.Status,
            caregiver.Availability,
            caregiver.DetailedAddress,
            caregiver.StatusReason,
            caregiver.MedicalProfile is null
                ? null
                : new MedicalProfileResponse(
                    caregiver.MedicalProfile.ProfessionalTitleId,
                    caregiver.MedicalProfile.YearsOfExperience,
                    caregiver.MedicalProfile.SpecializationId,
                    caregiver.MedicalProfile.AcademicDegreeId,
                    caregiver.MedicalProfile.CurrentWorkplace,
                    caregiver.MedicalProfile.Biography),
            caregiver.CompanionProfile is null
                ? null
                : new CompanionProfileResponse(
                    caregiver.CompanionProfile.YearsOfExperience,
                    caregiver.CompanionProfile.SpecializationId,
                    caregiver.CompanionProfile.Biography),
            caregiver.Certificates
                .Select(certificate => new CertificateItemResponse(
                    certificate.Id,
                    certificate.Type,
                    certificate.ExpiryDate,
                    certificate.VerificationStatus,
                    certificate.ReviewReason))
                .ToList(),
            caregiver.ServiceSelections
                .Select(selection => selection.Id)
                .ToList(),
            caregiver.LanguageSelections
                .Select(selection => selection.Id)
                .ToList(),
            caregiver.AreaSelections
                .Select(selection => selection.Id)
                .ToList(),
            caregiver.MedicalPricing is null
                ? null
                : new MedicalPricingResponse(
                    caregiver.MedicalPricing.HomeVisitPrice,
                    caregiver.MedicalPricing.EightHourShiftPrice,
                    caregiver.MedicalPricing.TwelveHourShiftPrice,
                    caregiver.MedicalPricing.TwentyFourHourShiftPrice),
            caregiver.CompanionPricing is null
                ? null
                : new CompanionPricingResponse(
                    caregiver.CompanionPricing.HourlyPrice,
                    caregiver.CompanionPricing.EightHourDayPrice,
                    caregiver.CompanionPricing.OvernightPrice),
            caregiver.MedicalSchedule is null
                ? null
                : new MedicalScheduleResponse(
                    caregiver.MedicalSchedule.Shifts
                        .Select(shift => new MedicalShiftItemResponse(
                            shift.DayOfWeek,
                            shift.ShiftType))
                        .ToList(),
                    caregiver.MedicalSchedule.HomeVisitWindows
                        .Select(window => new MedicalHomeVisitWindowItemResponse(
                            window.DayOfWeek,
                            window.StartTime,
                            window.EndTime))
                        .ToList()),
            caregiver.CompanionSchedule is null
                ? null
                : new CompanionScheduleResponse(
                    caregiver.CompanionSchedule.Windows
                        .Select(window =>
                            new CompanionAvailabilityWindowItemResponse(
                                window.BookingType,
                                window.DayOfWeek,
                                window.StartTime,
                                window.EndTime))
                        .ToList()));
}

// ----------------------------- Bootstrap ------------------------------

public sealed record BootstrapCaregiverCommand(
    UserId UserId,
    CaregiverType CaregiverType)
    : ICommand<CaregiverProfileResponse>;

public sealed class BootstrapCaregiverCommandHandler
    : ICommandHandler<BootstrapCaregiverCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public BootstrapCaregiverCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        BootstrapCaregiverCommand request,
        CancellationToken cancellationToken)
    {
        bool exists =
            await _dbContext.Caregivers.AnyAsync(
                caregiver => caregiver.UserId == request.UserId,
                cancellationToken);

        if (exists)
        {
            return OnboardingErrors.AlreadyExists;
        }

        Caregiver caregiver =
            Caregiver.Create(
                request.UserId,
                request.CaregiverType);

        _dbContext.Caregivers.Add(caregiver);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}

// --------------------------- Get own profile --------------------------

public sealed record GetCaregiverProfileQuery(
    UserId UserId)
    : IQuery<CaregiverProfileResponse>;

public sealed class GetCaregiverProfileQueryHandler
    : IQueryHandler<GetCaregiverProfileQuery, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetCaregiverProfileQueryHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        GetCaregiverProfileQuery request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .AsNoTracking()
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.UserId == request.UserId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        return caregiver.ToProfileResponse();
    }
}

// ---------------------------- Medical profile -------------------------

public sealed record UpdateMedicalProfileCommand(
    UserId UserId,
    ProfessionalTitleId ProfessionalTitleId,
    int YearsOfExperience,
    SpecializationId SpecializationId,
    AcademicDegreeId AcademicDegreeId,
    string? CurrentWorkplace,
    string? Biography,
    DateTime UtcNow)
    : ICommand<CaregiverProfileResponse>;

public sealed class UpdateMedicalProfileCommandValidator
    : AbstractValidator<UpdateMedicalProfileCommand>
{
    public UpdateMedicalProfileCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.ProfessionalTitleId)
            .NotEqual(ProfessionalTitleId.Empty);
        RuleFor(c => c.SpecializationId)
            .NotEqual(SpecializationId.Empty);
        RuleFor(c => c.AcademicDegreeId)
            .NotEqual(AcademicDegreeId.Empty);
        RuleFor(c => c.YearsOfExperience).InclusiveBetween(0, 80);
        RuleFor(c => c.CurrentWorkplace)
            .MaximumLength(MedicalCaregiverProfile.MaximumWorkplaceLength)
            .When(c => c.CurrentWorkplace is not null);
        RuleFor(c => c.Biography)
            .MaximumLength(MedicalCaregiverProfile.MaximumBiographyLength)
            .When(c => c.Biography is not null);
    }
}

public sealed class UpdateMedicalProfileCommandHandler
    : ICommandHandler<UpdateMedicalProfileCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public UpdateMedicalProfileCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        UpdateMedicalProfileCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.UserId == request.UserId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        if (caregiver.Type != CaregiverType.Medical)
        {
            return OnboardingErrors.WrongCaregiverType;
        }

        var title =
            await _dbContext.ProfessionalTitles
                .SingleOrDefaultAsync(
                    t => t.Id == request.ProfessionalTitleId,
                    cancellationToken);

        if (title is null)
        {
            return LookupsErrors.NotFound;
        }

        if (!title.IsActive)
        {
            return OnboardingErrors.InactiveLookup;
        }

        var specialization =
            await _dbContext.Specializations
                .SingleOrDefaultAsync(
                    s => s.Id == request.SpecializationId,
                    cancellationToken);

        if (specialization is null)
        {
            return LookupsErrors.NotFound;
        }

        if (!specialization.IsActive ||
            specialization.CaregiverType != CaregiverType.Medical)
        {
            return OnboardingErrors.InactiveLookup;
        }

        var degree =
            await _dbContext.AcademicDegrees
                .SingleOrDefaultAsync(
                    d => d.Id == request.AcademicDegreeId,
                    cancellationToken);

        if (degree is null)
        {
            return LookupsErrors.NotFound;
        }

        if (!degree.IsActive)
        {
            return OnboardingErrors.InactiveLookup;
        }

        caregiver.UpdateMedicalProfile(
            title,
            request.YearsOfExperience,
            specialization,
            degree,
            NormalizeToNull(request.CurrentWorkplace),
            NormalizeToNull(request.Biography),
            request.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }

    private static string? NormalizeToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

// --------------------------- Companion profile ------------------------

public sealed record UpdateCompanionProfileCommand(
    UserId UserId,
    int YearsOfExperience,
    SpecializationId SpecializationId,
    string? Biography,
    DateTime UtcNow)
    : ICommand<CaregiverProfileResponse>;

public sealed class UpdateCompanionProfileCommandValidator
    : AbstractValidator<UpdateCompanionProfileCommand>
{
    public UpdateCompanionProfileCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.SpecializationId)
            .NotEqual(SpecializationId.Empty);
        RuleFor(c => c.YearsOfExperience).InclusiveBetween(0, 80);
        RuleFor(c => c.Biography)
            .MaximumLength(CompanionCaregiverProfile.MaximumBiographyLength)
            .When(c => c.Biography is not null);
    }
}

public sealed class UpdateCompanionProfileCommandHandler
    : ICommandHandler<UpdateCompanionProfileCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public UpdateCompanionProfileCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        UpdateCompanionProfileCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.UserId == request.UserId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        if (caregiver.Type != CaregiverType.Companion)
        {
            return OnboardingErrors.WrongCaregiverType;
        }

        var specialization =
            await _dbContext.Specializations
                .SingleOrDefaultAsync(
                    s => s.Id == request.SpecializationId,
                    cancellationToken);

        if (specialization is null)
        {
            return LookupsErrors.NotFound;
        }

        if (!specialization.IsActive ||
            specialization.CaregiverType != CaregiverType.Companion)
        {
            return OnboardingErrors.InactiveLookup;
        }

        caregiver.UpdateCompanionProfile(
            request.YearsOfExperience,
            specialization,
            string.IsNullOrWhiteSpace(request.Biography)
                ? null
                : request.Biography.Trim(),
            request.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}

// --------------------------- Detailed address -------------------------

public sealed record UpdateCaregiverAddressCommand(
    UserId UserId,
    string? DetailedAddress)
    : ICommand<CaregiverProfileResponse>;

public sealed class UpdateCaregiverAddressCommandValidator
    : AbstractValidator<UpdateCaregiverAddressCommand>
{
    public UpdateCaregiverAddressCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DetailedAddress)
            .MaximumLength(Caregiver.MaximumDetailedAddressLength)
            .When(c => c.DetailedAddress is not null);
    }
}

public sealed class UpdateCaregiverAddressCommandHandler
    : ICommandHandler<UpdateCaregiverAddressCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public UpdateCaregiverAddressCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        UpdateCaregiverAddressCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.UserId == request.UserId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        caregiver.UpdateDetailedAddress(
            string.IsNullOrWhiteSpace(request.DetailedAddress)
                ? null
                : request.DetailedAddress.Trim());

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}