using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Landing;

namespace Sanad.Modules.Cms.Application.Landing;

/// <summary>
/// Admin-facing section-kind and status tokens. The Admin contract speaks
/// kebab-case kind keys (<c>banners</c>, <c>quick-links</c>,
/// <c>featured-services</c>) and status names (<c>Draft</c>,
/// <c>Published</c>, <c>Archived</c>); enum names are accepted
/// case-insensitively on input, and a bare number never is - the numeric enum
/// value was never part of the agreed contract, and accepting it would let two
/// spellings of the same filter disagree.
/// </summary>
public static class FamilyHomeKindParser
{
    public static string ToKindKey(this FamilyHomeSectionKind kind) => kind switch
    {
        FamilyHomeSectionKind.Banners => "banners",
        FamilyHomeSectionKind.QuickLinks => "quick-links",
        FamilyHomeSectionKind.FeaturedServices => "featured-services",
        _ => throw new InvalidOperationException(
            $"Unknown family home section kind {(int)kind}.")
    };

    public static bool TryParseKind(string? value, out FamilyHomeSectionKind kind)
    {
        kind = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string token = value.Trim();

        foreach (FamilyHomeSectionKind candidate in Enum.GetValues<FamilyHomeSectionKind>())
        {
            if (string.Equals(candidate.ToKindKey(), token, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        if (token.Any(char.IsDigit))
            return false;

        return Enum.TryParse(token, ignoreCase: true, out kind) && Enum.IsDefined(kind);
    }

    public static bool TryParseStatus(string? value, out FamilyHomeSectionStatus status)
    {
        status = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string token = value.Trim();

        if (token.Any(char.IsDigit))
            return false;

        return Enum.TryParse(token, ignoreCase: true, out status) && Enum.IsDefined(status);
    }
}

public sealed record GetFamilyHomeAdminListQuery(
    FamilyHomeSectionStatus? Status = null,
    FamilyHomeSectionKind? Kind = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20)
    : IQuery<FamilyHomeAdminPageResponse>;

public sealed class GetFamilyHomeAdminListQueryHandler(ICmsDbContext db)
    : IQueryHandler<GetFamilyHomeAdminListQuery, FamilyHomeAdminPageResponse>
{
    public async Task<Result<FamilyHomeAdminPageResponse>> Handle(
        GetFamilyHomeAdminListQuery request, CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        IQueryable<FamilyHomeSection> query = db.FamilyHomeSections.AsNoTracking();

        if (request.Status is FamilyHomeSectionStatus status)
            query = query.Where(section => section.Status == status);

        if (request.Kind is FamilyHomeSectionKind kind)
            query = query.Where(section => section.Kind == kind);

        // Search runs over the stored typed payload, because a family home
        // section has no editorial title column of its own: its copy lives
        // inside the items.
        string? term = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim().ToLowerInvariant();
        if (term is not null)
        {
            query = query.Where(section =>
                section.ItemsJson != null &&
                section.ItemsJson.ToLower().Contains(term));
        }

        int totalCount = await query.CountAsync(cancellationToken);

        List<FamilyHomeSection> sections = await query
            .OrderBy(section => section.DisplayOrder)
            .ThenBy(section => section.CreatedOnUtc)
            .ThenBy(section => section.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<FamilyHomeAdminSectionResponse>();
        foreach (FamilyHomeSection section in sections)
        {
            Result<FamilyHomeAdminSectionResponse> mapped = section.ToAdminResponse();
            if (mapped.IsFailure)
                return Result<FamilyHomeAdminPageResponse>.Failure(mapped.Error);

            items.Add(mapped.Value);
        }

        return Result<FamilyHomeAdminPageResponse>.Success(
            new FamilyHomeAdminPageResponse(items, page, pageSize, totalCount));
    }
}

public sealed record GetFamilyHomeAdminSectionQuery(Guid Id)
    : IQuery<FamilyHomeAdminSectionResponse>;

public sealed class GetFamilyHomeAdminSectionQueryHandler(ICmsDbContext db)
    : IQueryHandler<GetFamilyHomeAdminSectionQuery, FamilyHomeAdminSectionResponse>
{
    public async Task<Result<FamilyHomeAdminSectionResponse>> Handle(
        GetFamilyHomeAdminSectionQuery request, CancellationToken cancellationToken)
    {
        FamilyHomeSection? section = await db.FamilyHomeSections
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == new FamilyHomeSectionId(request.Id), cancellationToken);
        if (section is null)
            return FamilyHomeErrors.SectionNotFound;

        return section.ToAdminResponse();
    }
}

public sealed record GetPublishedFamilyHomeSectionsQuery
    : IQuery<IReadOnlyList<FamilyHomeSectionPublicItem>>;

public sealed class GetPublishedFamilyHomeSectionsQueryHandler(
    ICmsDbContext db,
    ICaregiversDbContext caregiversDb)
    : IQueryHandler<GetPublishedFamilyHomeSectionsQuery, IReadOnlyList<FamilyHomeSectionPublicItem>>
{
    public async Task<Result<IReadOnlyList<FamilyHomeSectionPublicItem>>> Handle(
        GetPublishedFamilyHomeSectionsQuery request, CancellationToken cancellationToken)
    {
        List<FamilyHomeSection> sections = await db.FamilyHomeSections
            .AsNoTracking()
            .Where(section =>
                section.Status == FamilyHomeSectionStatus.Published &&
                section.IsVisible)
            .OrderBy(section => section.DisplayOrder)
            .ThenBy(section => section.CreatedOnUtc)
            .ThenBy(section => section.Id)
            .ToListAsync(cancellationToken);

        if (sections.Count == 0)
            return Result<IReadOnlyList<FamilyHomeSectionPublicItem>>.Success([]);

        var stored = new List<(FamilyHomeSection Section, IReadOnlyList<FamilyHomeItem> Items)>();
        foreach (FamilyHomeSection section in sections)
        {
            try
            {
                stored.Add((section, FamilyHomeItems.Deserialize(section.ItemsJson)));
            }
            catch (DomainException ex)
            {
                // A published section whose stored content cannot be read is a
                // bounded read failure, never a silently empty section.
                return FamilyHomeErrors.Incomplete(ex.Message);
            }
        }

        Dictionary<Guid, Service> activeServices = await LoadReferencedServicesAsync(
            stored, cancellationToken);

        var result = new List<FamilyHomeSectionPublicItem>();
        foreach ((FamilyHomeSection section, IReadOnlyList<FamilyHomeItem> items) in stored)
        {
            List<FamilyHomePublicItem> publicItems = section.Kind == FamilyHomeSectionKind.FeaturedServices
                ? BuildFeaturedItems(items, activeServices)
                : BuildEditorialItems(items, activeServices);

            if (publicItems.Count == 0)
                continue;

            result.Add(new FamilyHomeSectionPublicItem(
                section.Id.Value,
                section.Kind.ToKindKey(),
                section.DisplayOrder,
                publicItems));
        }

        return Result<IReadOnlyList<FamilyHomeSectionPublicItem>>.Success(result);
    }

    private async Task<Dictionary<Guid, Service>> LoadReferencedServicesAsync(
        IReadOnlyList<(FamilyHomeSection Section, IReadOnlyList<FamilyHomeItem> Items)> stored,
        CancellationToken cancellationToken)
    {
        List<ServiceId> serviceIds = ReferencedServiceIds(stored);

        if (serviceIds.Count == 0)
            return [];

        // One batched catalog read for the whole response instead of one query
        // per item.
        List<Service> services = await caregiversDb.Services
            .AsNoTracking()
            .Where(service => serviceIds.Contains(service.Id) && service.IsActive)
            .ToListAsync(cancellationToken);

        return services.ToDictionary(service => service.Id.Value);
    }

    private static List<ServiceId> ReferencedServiceIds(
        IReadOnlyList<(FamilyHomeSection Section, IReadOnlyList<FamilyHomeItem> Items)> stored)
    {
        var serviceIds = new List<ServiceId>();

        foreach ((FamilyHomeSection section, IReadOnlyList<FamilyHomeItem> items) in stored)
        {
            IEnumerable<Guid> referenced = section.Kind == FamilyHomeSectionKind.FeaturedServices
                ? FamilyHomeContentRules.FeaturedServiceIds(items)
                : FamilyHomeContentRules.TargetServiceIds(items);

            foreach (Guid id in referenced)
                serviceIds.Add(new ServiceId(id));
        }

        return serviceIds.Distinct().ToList();
    }

    private static List<FamilyHomePublicItem> BuildFeaturedItems(
        IReadOnlyList<FamilyHomeItem> items,
        Dictionary<Guid, Service> activeServices)
    {
        var publicItems = new List<FamilyHomePublicItem>();

        foreach (FamilyHomeItem item in items)
        {
            if (item.ServiceId is not { } serviceId ||
                serviceId == Guid.Empty ||
                !activeServices.TryGetValue(serviceId, out Service? service))
            {
                // A missing or inactive catalog service omits its item only.
                continue;
            }

            publicItems.Add(new FamilyHomePublicItem(
                null,
                null,
                null,
                null,
                null,
                null,
                FamilyHomeContentRules.ServiceTarget,
                serviceId,
                serviceId,
                service.ArabicName,
                service.EnglishName,
                service.IconPath,
                (int)service.CaregiverType));
        }

        return publicItems;
    }

    private static List<FamilyHomePublicItem> BuildEditorialItems(
        IReadOnlyList<FamilyHomeItem> items,
        Dictionary<Guid, Service> activeServices)
    {
        var publicItems = new List<FamilyHomePublicItem>();

        foreach (FamilyHomeItem item in items)
        {
            string? targetType = item.Target?.Type;
            Guid? targetServiceId = item.Target?.ServiceId;

            if (string.Equals(
                    targetType,
                    FamilyHomeContentRules.ServiceTarget,
                    StringComparison.Ordinal) &&
                (targetServiceId is null ||
                    !activeServices.ContainsKey(targetServiceId.Value)))
            {
                // An action-bearing item whose target is unavailable is omitted
                // with its item, so the client never renders a dead action.
                continue;
            }

            publicItems.Add(new FamilyHomePublicItem(
                item.ArabicTitle,
                item.EnglishTitle,
                item.ArabicBody,
                item.EnglishBody,
                item.ImageKey,
                item.IconKey,
                targetType,
                targetServiceId,
                null,
                null,
                null,
                null,
                null));
        }

        return publicItems;
    }
}

internal static class FamilyHomeSectionMappings
{
    /// <summary>
    /// The single Admin projection for a family home section. The stored item
    /// payload is parsed into typed items here, and unreadable content fails the
    /// read instead of being reported as an empty section.
    /// </summary>
    public static Result<FamilyHomeAdminSectionResponse> ToAdminResponse(
        this FamilyHomeSection section)
    {
        IReadOnlyList<FamilyHomeItem> items;

        try
        {
            items = FamilyHomeItems.Deserialize(section.ItemsJson);
        }
        catch (DomainException ex)
        {
            return FamilyHomeErrors.Incomplete(ex.Message);
        }

        return new FamilyHomeAdminSectionResponse(
            section.Id.Value,
            section.Kind.ToKindKey(),
            section.Version,
            section.Status.ToString(),
            section.DisplayOrder,
            section.IsVisible,
            section.SourceId?.Value,
            items,
            section.CreatedOnUtc,
            section.UpdatedOnUtc,
            section.PublishedOnUtc);
    }
}
