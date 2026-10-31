using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Settings;

namespace Ahlena.Modules.Cms.Application.Settings;

/// <summary>
/// Reads the one site-settings row for the admin authoring surface.
/// </summary>
public sealed record GetAdminSiteSettingsQuery
    : IQuery<SiteSettingsAdminResponse>;

public sealed class GetAdminSiteSettingsQueryHandler(
    ICmsDbContext db)
    : IQueryHandler<GetAdminSiteSettingsQuery, SiteSettingsAdminResponse>
{
    public async Task<Result<SiteSettingsAdminResponse>> Handle(
        GetAdminSiteSettingsQuery request,
        CancellationToken cancellationToken)
    {
        SiteSettings? settings = await db.SiteSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == SiteSettingsId.Singleton,
                cancellationToken);

        if (settings is null)
            return Result<SiteSettingsAdminResponse>.Failure(SiteSettingsErrors.NotFound);

        return SiteSettingsMapping.ToAdminResponse(settings);
    }
}

/// <summary>
/// Reads the published site settings for the anonymous app surface. Before
/// anything is authored the read is honest: no navbar links and no SEO
/// defaults, never invented content.
/// </summary>
public sealed record GetPublicSiteSettingsQuery
    : IQuery<PublicSiteSettingsResponse>;

public sealed class GetPublicSiteSettingsQueryHandler(
    ICmsDbContext db)
    : IQueryHandler<GetPublicSiteSettingsQuery, PublicSiteSettingsResponse>
{
    public async Task<Result<PublicSiteSettingsResponse>> Handle(
        GetPublicSiteSettingsQuery request,
        CancellationToken cancellationToken)
    {
        SiteSettings? settings = await db.SiteSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == SiteSettingsId.Singleton,
                cancellationToken);

        if (settings is null)
            return Result<PublicSiteSettingsResponse>.Success(new PublicSiteSettingsResponse([], null));

        return Result<PublicSiteSettingsResponse>.Success(new PublicSiteSettingsResponse(
            SiteSettingsNavbarLinks.Deserialize(settings.NavbarLinksJson),
            SiteSettingsMapping.ToSeo(settings)));
    }
}
