using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Cms.Domain.Settings;

namespace Sanad.Modules.Cms.Application.Settings;

/// <summary>
/// One authored navbar link. The key is the stable identifier an app client
/// uses for the link; the labels are the bilingual display text and the url is
/// the navigation target. The authored array order is the navbar order.
/// </summary>
public sealed record SiteSettingsNavbarLink(
    string Key,
    string ArabicLabel,
    string EnglishLabel,
    string Url);

/// <summary>
/// The authored SEO defaults. Every member is optional: a member that was not
/// authored stays absent, and the whole SEO block is absent when no member was
/// authored.
/// </summary>
public sealed record SiteSettingsSeoResponse(
    string? ArabicTitle,
    string? EnglishTitle,
    string? ArabicDescription,
    string? EnglishDescription);

/// <summary>
/// The admin read/update shape of the one settings row. It carries the
/// authoring timestamps; the public read does not.
/// </summary>
public sealed record SiteSettingsAdminResponse(
    IReadOnlyList<SiteSettingsNavbarLink> NavbarLinks,
    SiteSettingsSeoResponse? Seo,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

/// <summary>
/// The public read shape. It exposes only the published settings content:
/// the navbar links and the SEO defaults. No identity, no timestamps and no
/// authoring metadata are exposed.
/// </summary>
public sealed record PublicSiteSettingsResponse(
    IReadOnlyList<SiteSettingsNavbarLink> NavbarLinks,
    SiteSettingsSeoResponse? Seo);

/// <summary>
/// The bounded error codes of the site-settings surface.
/// </summary>
public static class SiteSettingsErrors
{
    public const string InvalidCode = "Cms.SiteSettings.Invalid";
    public const string NotFoundCode = "Cms.SiteSettings.NotFound";
    public const string ConflictCode = "Cms.SiteSettings.Conflict";

    /// <summary>
    /// The authored content is invalid (an unusable link target, a duplicated
    /// link key, an oversized value). The reason is carried in the message
    /// for logs; the API response stays generic.
    /// </summary>
    public static Error Invalid(string reason) =>
        new(InvalidCode, reason);

    /// <summary>Nothing has been authored yet, so there is no settings row.</summary>
    public static readonly Error NotFound =
        new(NotFoundCode, "Site settings have not been authored yet.");

    /// <summary>
    /// Another writer created the single row first. The write is refused
    /// instead of creating a second row.
    /// </summary>
    public static readonly Error Conflict =
        new(ConflictCode, "Site settings were authored concurrently; retry the update.");
}

/// <summary>
/// Maps the stored settings row onto the read shapes.
/// </summary>
internal static class SiteSettingsMapping
{
    public static SiteSettingsAdminResponse ToAdminResponse(SiteSettings settings) =>
        new(
            SiteSettingsNavbarLinks.Deserialize(settings.NavbarLinksJson),
            ToSeo(settings),
            settings.CreatedOnUtc,
            settings.UpdatedOnUtc);

    public static SiteSettingsSeoResponse? ToSeo(SiteSettings settings)
    {
        if (settings.ArabicSeoTitle is null &&
            settings.EnglishSeoTitle is null &&
            settings.ArabicSeoDescription is null &&
            settings.EnglishSeoDescription is null)
        {
            return null;
        }

        return new SiteSettingsSeoResponse(
            settings.ArabicSeoTitle,
            settings.EnglishSeoTitle,
            settings.ArabicSeoDescription,
            settings.EnglishSeoDescription);
    }
}

/// <summary>
/// Normalizes, validates and serializes the authored navbar links. The link
/// rules live here so that no entry point can store an unsafe target.
/// </summary>
internal static class SiteSettingsNavbarLinks
{
    // The relaxed encoder is deliberate: the stored labels are Arabic, and the
    // default encoder would store them as "\u0645..." escapes. The value is
    // only ever deserialized back into typed records, never emitted as markup.
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

    /// <summary>
    /// Validates and normalizes the authored links. Every link needs a key, an
    /// Arabic label, an English label and a usable target.
    /// </summary>
    public static IReadOnlyList<SiteSettingsNavbarLink> Normalize(
        IReadOnlyList<SiteSettingsNavbarLinkInput>? inputs)
    {
        var links = new List<SiteSettingsNavbarLink>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (SiteSettingsNavbarLinkInput input in inputs ?? [])
        {
            string key = Required(input.Key, SiteSettings.MaximumLinkKeyLength, "Navbar link key");
            if (!IsKeyCharacters(key))
                throw new DomainException("Navbar link key may only contain letters, digits, '-', '_' and '.'.");
            if (!keys.Add(key))
                throw new DomainException($"Navbar link key '{key}' is used more than once.");

            string arabicLabel = Required(input.ArabicLabel, SiteSettings.MaximumLinkLabelLength, "Arabic navbar link label");
            string englishLabel = Required(input.EnglishLabel, SiteSettings.MaximumLinkLabelLength, "English navbar link label");
            string url = NormalizeUrl(input.Url);

            links.Add(new SiteSettingsNavbarLink(key, arabicLabel, englishLabel, url));
        }

        if (links.Count > SiteSettings.MaximumLinkCount)
            throw new DomainException($"At most {SiteSettings.MaximumLinkCount} navbar links are allowed.");

        return links;
    }

    public static string Serialize(IReadOnlyList<SiteSettingsNavbarLink> links) =>
        JsonSerializer.Serialize(links, Options);

    public static IReadOnlyList<SiteSettingsNavbarLink> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<SiteSettingsNavbarLink>>(json, Options) ?? [];
        }
        catch (JsonException exception)
        {
            throw new DomainException($"Stored navbar links are not readable: {exception.Message}");
        }
    }

    private static string Required(
        string? value,
        int maximumLength,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException($"{fieldName} is required.");

        string normalized = value.Trim();

        if (normalized.Length > maximumLength)
            throw new DomainException($"{fieldName} cannot exceed {maximumLength} characters.");

        return normalized;
    }

    private static bool IsKeyCharacters(string key)
    {
        foreach (char character in key)
        {
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not '-' &&
                character is not '_' &&
                character is not '.')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A navigation target is either a root-relative app route or an absolute
    /// http(s) url. Everything else is refused: <c>javascript:</c>,
    /// <c>data:</c> and other schemes, and protocol-relative targets such as
    /// <c>//host/path</c>.
    /// </summary>
    private static string NormalizeUrl(string? value)
    {
        string url = Required(value, SiteSettings.MaximumLinkUrlLength, "Navbar link url");

        foreach (char character in url)
        {
            if (char.IsControl(character))
                throw new DomainException("Navbar link url must not contain control characters.");
        }

        if (url.StartsWith("//", StringComparison.Ordinal) ||
            url.StartsWith("/\\", StringComparison.Ordinal))
        {
            throw new DomainException("Navbar link url must not be a protocol-relative url.");
        }

        if (url.StartsWith("/", StringComparison.Ordinal))
            return url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException("Navbar link url must be a root-relative path or an absolute http(s) url.");
        }

        return url;
    }
}
