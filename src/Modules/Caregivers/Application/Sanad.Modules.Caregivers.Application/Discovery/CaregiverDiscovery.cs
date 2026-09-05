using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Discovery;

// ================================= Models & DTOs =================================

public sealed record CaregiverSearchCardResponse(
    Guid Id,
    Guid UserId,
    string ArabicFullName,
    string EnglishFullName,
    Gender? Gender,
    string? AvatarUrl,
    CaregiverType Type,
    string? ProfessionalTitleAr,
    string? ProfessionalTitleEn,
    int ExperienceYears,
    decimal StartingPrice,
    decimal AverageRating,
    int ReviewsCount,
    CaregiverAvailability Availability,
    IReadOnlyList<string> SpecializationsAr,
    IReadOnlyList<string> SpecializationsEn,
    IReadOnlyList<string> WorkingAreasAr);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record CaregiverSpecializationItemResponse(
    Guid Id,
    string ArabicName,
    string EnglishName);

public sealed record CaregiverAreaItemResponse(
    Guid Id,
    string ArabicName,
    string EnglishName,
    string? CityArabicName,
    string? CityEnglishName);

public sealed record CaregiverServiceItemResponse(
    Guid Id,
    string ArabicName,
    string EnglishName,
    string IconPath);

public sealed record CaregiverLanguageItemResponse(
    Guid Id,
    string ArabicName,
    string EnglishName,
    string Code);

public sealed record CaregiverPublicCertificateResponse(
    CaregiverCertificateType Type,
    DateOnly? ExpiryDate,
    CertificateVerificationStatus VerificationStatus);

public sealed record CaregiverPublicScheduleSlotResponse(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed record CaregiverPublicScheduleResponse(
    IReadOnlyList<CaregiverPublicScheduleSlotResponse> ActiveSlots);

public sealed record CaregiverPublicPricingResponse(
    decimal? HourlyPrice,
    decimal? EightHourDayPrice,
    decimal? TwelveHourShiftPrice,
    decimal? TwentyFourHourPrice,
    decimal? HomeVisitPrice);

public sealed record CaregiverPublicProfileResponse(
    Guid Id,
    Guid UserId,
    string ArabicFullName,
    string EnglishFullName,
    Gender? Gender,
    string? AvatarUrl,
    CaregiverType Type,
    string? ProfessionalTitleAr,
    string? ProfessionalTitleEn,
    string? AcademicDegreeAr,
    string? AcademicDegreeEn,
    int ExperienceYears,
    string? Bio,
    string? Workplace,
    decimal AverageRating,
    int ReviewsCount,
    CaregiverAvailability Availability,
    CaregiverPublicPricingResponse Pricing,
    IReadOnlyList<CaregiverSpecializationItemResponse> Specializations,
    IReadOnlyList<CaregiverAreaItemResponse> WorkingAreas,
    IReadOnlyList<CaregiverServiceItemResponse> Services,
    IReadOnlyList<CaregiverLanguageItemResponse> Languages,
    IReadOnlyList<CaregiverPublicCertificateResponse> VerifiedCertificates,
    CaregiverPublicScheduleResponse Schedule);

// ================================= Search Query =================================

public sealed record SearchCaregiversQuery(
    string? Search = null,
    CaregiverType? Type = null,
    Gender? Gender = null,
    Guid? GovernorateId = null,
    Guid? CityId = null,
    Guid? AreaId = null,
    Guid? SpecializationId = null,
    CaregiverAvailability? Availability = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    decimal? MinRating = null,
    int? MinExperienceYears = null,
    int Page = 1,
    int PageSize = 10) : IQuery<PagedResult<CaregiverSearchCardResponse>>;

public sealed class SearchCaregiversQueryValidator : AbstractValidator<SearchCaregiversQuery>
{
    public SearchCaregiversQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThan(0);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 50);
        RuleFor(q => q.MinPrice).GreaterThanOrEqualTo(0).When(q => q.MinPrice.HasValue);
        RuleFor(q => q.MaxPrice).GreaterThanOrEqualTo(0).When(q => q.MaxPrice.HasValue);
        RuleFor(q => q.MinRating).InclusiveBetween(0, 5).When(q => q.MinRating.HasValue);
        RuleFor(q => q.MinExperienceYears).GreaterThanOrEqualTo(0).When(q => q.MinExperienceYears.HasValue);
    }
}

public sealed class SearchCaregiversQueryHandler : IQueryHandler<SearchCaregiversQuery, PagedResult<CaregiverSearchCardResponse>>
{
    private readonly ICaregiversDbContext _dbContext;

    public SearchCaregiversQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedResult<CaregiverSearchCardResponse>>> Handle(
        SearchCaregiversQuery request,
        CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _dbContext.SearchActiveCaregiversAsync(
            request.Search,
            request.Type.HasValue ? (int)request.Type.Value : null,
            request.Gender.HasValue ? (int)request.Gender.Value : null,
            request.AreaId,
            request.SpecializationId,
            request.Availability.HasValue ? (int)request.Availability.Value : null,
            request.MinPrice,
            request.MaxPrice,
            request.MinRating,
            request.MinExperienceYears,
            request.Page,
            request.PageSize,
            cancellationToken);

        int totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        return Result<PagedResult<CaregiverSearchCardResponse>>.Success(
            new PagedResult<CaregiverSearchCardResponse>(items, request.Page, request.PageSize, totalCount, totalPages));
    }
}

// ============================= Profile Detail Query =============================

public sealed record GetCaregiverPublicProfileQuery(
    CaregiverId CaregiverId) : IQuery<CaregiverPublicProfileResponse>;

public sealed class GetCaregiverPublicProfileQueryHandler : IQueryHandler<GetCaregiverPublicProfileQuery, CaregiverPublicProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public GetCaregiverPublicProfileQueryHandler(ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverPublicProfileResponse>> Handle(
        GetCaregiverPublicProfileQuery request,
        CancellationToken cancellationToken)
    {
        // 1. Fetch Caregiver Aggregate from DB
        var caregiver = await _dbContext.Caregivers
            .AsNoTracking()
            .Include(c => c.Certificates)
            .SingleOrDefaultAsync(c => c.Id == request.CaregiverId && c.Status == CaregiverStatus.Active, cancellationToken);

        if (caregiver is null)
        {
            return Result<CaregiverPublicProfileResponse>.Failure(
                new Error("Caregivers.Discovery.NotFound", "Caregiver was not found or is currently inactive."));
        }

        // 2. Fetch User Identity Record from DB
        CaregiverUserHeader? userHeader = await _dbContext.GetCaregiverUserHeaderAsync(caregiver.UserId, cancellationToken);
        if (userHeader is null)
        {
            return Result<CaregiverPublicProfileResponse>.Failure(
                new Error("Caregivers.Discovery.UserNotFound", "Caregiver identity account not found."));
        }

        // 3. Resolve Lookups from DB
        var specializations = await _dbContext.Specializations.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        var professionalTitles = await _dbContext.ProfessionalTitles.AsNoTracking().ToDictionaryAsync(t => t.Id, cancellationToken);
        var academicDegrees = await _dbContext.AcademicDegrees.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);
        var areas = await _dbContext.Areas.AsNoTracking().ToDictionaryAsync(a => a.Id, cancellationToken);
        var cities = await _dbContext.Cities.AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        var services = await _dbContext.Services.AsNoTracking().ToDictionaryAsync(s => s.Id, cancellationToken);
        var languages = await _dbContext.Languages.AsNoTracking().ToDictionaryAsync(l => l.Id, cancellationToken);

        string? titleAr = null, titleEn = null, degreeAr = null, degreeEn = null;
        string? bio = caregiver.MedicalProfile?.Biography ?? caregiver.CompanionProfile?.Biography;
        string? workplace = caregiver.MedicalProfile?.CurrentWorkplace;
        int expYears = caregiver.MedicalProfile?.YearsOfExperience ?? caregiver.CompanionProfile?.YearsOfExperience ?? 0;

        if (caregiver.MedicalProfile != null && professionalTitles.TryGetValue(caregiver.MedicalProfile.ProfessionalTitleId, out var title))
        {
            titleAr = title.ArabicName;
            titleEn = title.EnglishName;
        }

        if (caregiver.MedicalProfile != null && academicDegrees.TryGetValue(caregiver.MedicalProfile.AcademicDegreeId, out var degree))
        {
            degreeAr = degree.ArabicName;
            degreeEn = degree.EnglishName;
        }

        var pricing = new CaregiverPublicPricingResponse(
            caregiver.CompanionPricing?.HourlyPrice,
            caregiver.CompanionPricing?.EightHourDayPrice ?? caregiver.MedicalPricing?.EightHourShiftPrice,
            caregiver.MedicalPricing?.TwelveHourShiftPrice,
            caregiver.MedicalPricing?.TwentyFourHourShiftPrice,
            caregiver.MedicalPricing?.HomeVisitPrice);

        var specsList = new List<CaregiverSpecializationItemResponse>();
        SpecializationId? specId = caregiver.MedicalProfile?.SpecializationId ?? caregiver.CompanionProfile?.SpecializationId;
        if (specId.HasValue && specializations.TryGetValue(specId.Value, out var sp))
        {
            specsList.Add(new CaregiverSpecializationItemResponse(sp.Id.Value, sp.ArabicName, sp.EnglishName));
        }

        var areasList = caregiver.AreaSelections
            .Where(s => areas.ContainsKey(s.Id))
            .Select(s =>
            {
                var area = areas[s.Id];
                cities.TryGetValue(area.CityId, out var city);
                return new CaregiverAreaItemResponse(
                    area.Id.Value,
                    area.ArabicName,
                    area.EnglishName,
                    city?.ArabicName,
                    city?.EnglishName);
            })
            .ToList();

        var servicesList = caregiver.ServiceSelections
            .Where(s => services.ContainsKey(s.Id))
            .Select(s => new CaregiverServiceItemResponse(
                s.Id.Value,
                services[s.Id].ArabicName,
                services[s.Id].EnglishName,
                services[s.Id].IconPath))
            .ToList();

        var languagesList = caregiver.LanguageSelections
            .Where(l => languages.ContainsKey(l.Id))
            .Select(l => new CaregiverLanguageItemResponse(
                l.Id.Value,
                languages[l.Id].ArabicName,
                languages[l.Id].EnglishName,
                languages[l.Id].Code))
            .ToList();

        var verifiedCerts = caregiver.Certificates
            .Where(c => c.VerificationStatus == CertificateVerificationStatus.Verified)
            .Select(c => new CaregiverPublicCertificateResponse(
                c.Type,
                c.ExpiryDate,
                c.VerificationStatus))
            .ToList();

        // 4. Extract Real Active Schedule Slots from Database
        var activeSlots = new List<CaregiverPublicScheduleSlotResponse>();

        if (caregiver.Type == CaregiverType.Medical && caregiver.MedicalSchedule != null)
        {
            foreach (var window in caregiver.MedicalSchedule.HomeVisitWindows)
            {
                activeSlots.Add(new CaregiverPublicScheduleSlotResponse(
                    window.DayOfWeek,
                    window.StartTime,
                    window.EndTime));
            }
        }
        else if (caregiver.Type == CaregiverType.Companion && caregiver.CompanionSchedule != null)
        {
            foreach (var window in caregiver.CompanionSchedule.Windows)
            {
                activeSlots.Add(new CaregiverPublicScheduleSlotResponse(
                    window.DayOfWeek,
                    window.StartTime,
                    window.EndTime));
            }
        }

        var schedule = new CaregiverPublicScheduleResponse(activeSlots);

        var response = new CaregiverPublicProfileResponse(
            caregiver.Id.Value,
            caregiver.UserId.Value,
            userHeader.ArabicFullName,
            userHeader.EnglishFullName,
            userHeader.Gender,
            userHeader.AvatarUrl,
            caregiver.Type,
            titleAr,
            titleEn,
            degreeAr,
            degreeEn,
            expYears,
            bio,
            workplace,
            caregiver.AverageRating,
            caregiver.ReviewsCount,
            caregiver.Availability,
            pricing,
            specsList,
            areasList,
            servicesList,
            languagesList,
            verifiedCerts,
            schedule);

        return Result<CaregiverPublicProfileResponse>.Success(response);
    }
}