using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Settings;

namespace Sanad.Modules.Cms.Application.Settings;

/// <summary>
/// One authored navbar link as it arrives from the admin client. Every member
/// is optional on the wire so that the validation reports the exact problem
/// instead of failing on a missing member.
/// </summary>
public sealed record SiteSettingsNavbarLinkInput(
    string? Key = null,
    string? ArabicLabel = null,
    string? EnglishLabel = null,
    string? Url = null);

/// <summary>
/// The authored SEO defaults as they arrive from the admin client.
/// </summary>
public sealed record SiteSettingsSeoInput(
    string? ArabicTitle = null,
    string? EnglishTitle = null,
    string? ArabicDescription = null,
    string? EnglishDescription = null);

/// <summary>
/// Replaces the whole site-settings document. The command creates the single
/// row on the first write and updates that same row afterwards; it never
/// creates a second row.
/// </summary>
public sealed record UpdateSiteSettingsCommand(
    IReadOnlyList<SiteSettingsNavbarLinkInput>? NavbarLinks = null,
    SiteSettingsSeoInput? Seo = null)
    : ICommand<SiteSettingsAdminResponse>;

public sealed class UpdateSiteSettingsCommandHandler(
    ICmsDbContext db)
    : ICommandHandler<UpdateSiteSettingsCommand, SiteSettingsAdminResponse>
{
    public async Task<Result<SiteSettingsAdminResponse>> Handle(
        UpdateSiteSettingsCommand request,
        CancellationToken cancellationToken)
    {
        SiteSettings? settings = await db.SiteSettings
            .SingleOrDefaultAsync(
                item => item.Id == SiteSettingsId.Singleton,
                cancellationToken);

        try
        {
            IReadOnlyList<SiteSettingsNavbarLink> links =
                SiteSettingsNavbarLinks.Normalize(request.NavbarLinks);
            string linksJson = SiteSettingsNavbarLinks.Serialize(links);
            SiteSettingsSeoInput seo = request.Seo ?? new SiteSettingsSeoInput();

            if (settings is null)
            {
                settings = SiteSettings.Create(
                    linksJson,
                    seo.ArabicTitle,
                    seo.EnglishTitle,
                    seo.ArabicDescription,
                    seo.EnglishDescription);
                db.SiteSettings.Add(settings);
            }
            else
            {
                settings.Update(
                    linksJson,
                    seo.ArabicTitle,
                    seo.EnglishTitle,
                    seo.ArabicDescription,
                    seo.EnglishDescription);
            }

            await db.SaveChangesAsync(cancellationToken);

            return SiteSettingsMapping.ToAdminResponse(settings);
        }
        catch (DomainException exception)
        {
            return Result<SiteSettingsAdminResponse>.Failure(
                SiteSettingsErrors.Invalid(exception.Message));
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            // Another writer created the single row first. The write is
            // refused with a conflict instead of creating a second row, and
            // the caller can read the row and retry the update.
            return Result<SiteSettingsAdminResponse>.Failure(
                SiteSettingsErrors.Conflict);
        }
    }

    private static bool IsDuplicateKey(DbUpdateException exception)
    {
        string text = exception.ToString();

        return text.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("site_settings", StringComparison.OrdinalIgnoreCase);
    }
}
