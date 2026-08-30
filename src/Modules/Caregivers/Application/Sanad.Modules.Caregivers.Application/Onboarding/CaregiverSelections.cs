using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Application.Onboarding;

public sealed record UpdateCaregiverSelectionsCommand(
    UserId UserId,
    IReadOnlyList<Guid> ServiceIds,
    IReadOnlyList<Guid> LanguageIds,
    IReadOnlyList<Guid> AreaIds)
    : ICommand<CaregiverProfileResponse>;

public sealed class UpdateCaregiverSelectionsCommandValidator
    : AbstractValidator<UpdateCaregiverSelectionsCommand>
{
    public UpdateCaregiverSelectionsCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.ServiceIds).NotNull();
        RuleFor(c => c.LanguageIds).NotNull();
        RuleFor(c => c.AreaIds).NotNull();
        RuleFor(c => c.AreaIds.Count)
            .LessThanOrEqualTo(Caregiver.MaximumAreaSelections)
            .When(c => c.AreaIds is not null);
    }
}

public sealed class UpdateCaregiverSelectionsCommandHandler
    : ICommandHandler<UpdateCaregiverSelectionsCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public UpdateCaregiverSelectionsCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        UpdateCaregiverSelectionsCommand request,
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

        List<ServiceId> desiredServiceIds =
            request.ServiceIds
                .Distinct()
                .Select(id => new ServiceId(id))
                .ToList();

        List<LanguageId> desiredLanguageIds =
            request.LanguageIds
                .Distinct()
                .Select(id => new LanguageId(id))
                .ToList();

        List<AreaId> desiredAreaIds =
            request.AreaIds
                .Distinct()
                .Select(id => new AreaId(id))
                .ToList();

        // Services: every id must exist, be active, and match the caregiver type.
        List<Service> services =
            await _dbContext.Services
                .Where(s => desiredServiceIds.Contains(s.Id))
                .ToListAsync(cancellationToken);

        if (services.Count != desiredServiceIds.Count)
        {
            return LookupsErrors.NotFound;
        }

        if (services.Any(s =>
                !s.IsActive ||
                s.CaregiverType != caregiver.Type))
        {
            return OnboardingErrors.InactiveLookup;
        }

        // Languages: exist + active (languages are type-neutral).
        List<Language> languages =
            await _dbContext.Languages
                .Where(l => desiredLanguageIds.Contains(l.Id))
                .ToListAsync(cancellationToken);

        if (languages.Count != desiredLanguageIds.Count)
        {
            return LookupsErrors.NotFound;
        }

        if (languages.Any(l => !l.IsActive))
        {
            return OnboardingErrors.InactiveLookup;
        }

        // Areas: exist + active with the FULL active parent chain
        // (area → city → governorate), same rule as the public chain lookups.
        List<Area> areas =
            await _dbContext.Areas
                .Where(a => desiredAreaIds.Contains(a.Id))
                .ToListAsync(cancellationToken);

        if (areas.Count != desiredAreaIds.Count)
        {
            return LookupsErrors.NotFound;
        }

        if (areas.Any(a => !a.IsActive))
        {
            return OnboardingErrors.InactiveLookup;
        }

        List<Guid> activeCityIds =
            areas.Select(a => a.CityId).Distinct()
                .Select(id => id.Value)
                .ToList();

        List<City> chainCities =
            await _dbContext.Cities
                .Where(c => activeCityIds.Contains(c.Id.Value))
                .ToListAsync(cancellationToken);

        if (chainCities.Count != activeCityIds.Count ||
            chainCities.Any(c => !c.IsActive))
        {
            return OnboardingErrors.InactiveLookup;
        }

        List<Guid> activeGovernorateIds =
            chainCities.Select(c => c.GovernorateId).Distinct()
                .Select(id => id.Value)
                .ToList();

        List<Governorate> chainGovernorates =
            await _dbContext.Governorates
                .Where(g => activeGovernorateIds.Contains(g.Id.Value))
                .ToListAsync(cancellationToken);

        if (chainGovernorates.Count != activeGovernorateIds.Count ||
            chainGovernorates.Any(g => !g.IsActive))
        {
            return OnboardingErrors.InactiveLookup;
        }

        // Diff and apply: remove selections no longer desired, then add new ones.
        foreach (ServiceId serviceId in
                 caregiver.ServiceSelections
                     .Select(s => s.Id)
                     .Where(id => !desiredServiceIds.Contains(id))
                     .ToList())
        {
            caregiver.RemoveService(serviceId);
        }

        foreach (Service service in
                 services.Where(s =>
                     !caregiver.ServiceSelections
                         .Any(selection => selection.Id == s.Id)))
        {
            caregiver.SelectService(service);
        }

        foreach (LanguageId languageId in
                 caregiver.LanguageSelections
                     .Select(l => l.Id)
                     .Where(id => !desiredLanguageIds.Contains(id))
                     .ToList())
        {
            caregiver.RemoveLanguage(languageId);
        }

        foreach (Language language in
                 languages.Where(l =>
                     !caregiver.LanguageSelections
                         .Any(selection => selection.Id == l.Id)))
        {
            caregiver.SelectLanguage(language);
        }

        foreach (AreaId areaId in
                 caregiver.AreaSelections
                     .Select(a => a.Id)
                     .Where(id => !desiredAreaIds.Contains(id))
                     .ToList())
        {
            caregiver.RemoveArea(areaId);
        }

        foreach (Area area in
                 areas.Where(a =>
                     !caregiver.AreaSelections
                         .Any(selection => selection.Id == a.Id)))
        {
            caregiver.SelectArea(area);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}