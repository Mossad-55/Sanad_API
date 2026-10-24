using System.Text.Json;
using Sanad.Modules.Cms.Application.Library;
using Sanad.Modules.Cms.Domain.Library;

namespace Sanad.API.Controllers.Requests;

public sealed record LibraryContentSectionRequest(
    int DisplayOrder,
    string ArabicTitle,
    string EnglishTitle,
    List<string>? ArabicBullets,
    List<string>? EnglishBullets);

/// <summary>
/// Library-scoped sections parsing and content validation. Accepts both the
/// existing PascalCase keys and the documented camelCase keys, keeps default
/// JSON number handling, and reports safe fixed field errors without leaking
/// parser or domain messages. Domain validation stays the final invariant
/// check in the command handlers.
/// </summary>
public static class LibraryContentValidation
{
    private static readonly JsonSerializerOptions SectionsOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool TryParseSections(
        string json,
        out IReadOnlyList<LibraryContentSectionInput>? sections,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        sections = null;

        List<LibraryContentSectionRequest?>? requests;
        try
        {
            requests = JsonSerializer.Deserialize<List<LibraryContentSectionRequest?>>(json, SectionsOptions);
        }
        catch (JsonException)
        {
            errors["sectionsJson"] = ["The sections content must be a JSON array of section objects."];
            return false;
        }
        catch (NotSupportedException)
        {
            errors["sectionsJson"] = ["The sections content must be a JSON array of section objects."];
            return false;
        }

        requests ??= [];
        for (int index = 0; index < requests.Count; index++)
        {
            if (requests[index] is null)
                errors[$"sectionsJson[{index}]"] = ["Each section must be an object."];
        }

        if (errors.Count > 0)
            return false;

        sections = requests
            .Select(item => new LibraryContentSectionInput(
                item!.DisplayOrder,
                item.ArabicTitle,
                item.EnglishTitle,
                item.ArabicBullets,
                item.EnglishBullets))
            .ToList();
        return true;
    }

    /// <summary>
    /// Validates the effective content against the effective (persisted on
    /// update) type. Returns null when valid; otherwise fixed safe field
    /// errors keyed by form field or indexed section path.
    /// </summary>
    public static Dictionary<string, string[]>? Validate(
        LibraryContentType effectiveType,
        string arabicTitle,
        string englishTitle,
        string arabicSummary,
        string englishSummary,
        string? videoUrl,
        int? durationSeconds,
        IReadOnlyList<LibraryContentSectionInput> effectiveSections)
    {
        var errors = new Dictionary<string, string[]>();
        AddTextError(errors, "arabicTitle", arabicTitle, LibraryContent.MaximumTitleLength);
        AddTextError(errors, "englishTitle", englishTitle, LibraryContent.MaximumTitleLength);
        AddTextError(errors, "arabicSummary", arabicSummary, LibraryContent.MaximumSummaryLength);
        AddTextError(errors, "englishSummary", englishSummary, LibraryContent.MaximumSummaryLength);

        if (effectiveType == LibraryContentType.Video)
        {
            // The legacy external URL is optional metadata only: playback comes
            // from uploaded bytes, so a missing URL is no longer a defect. When
            // one is supplied it must still be a well-formed absolute http(s)
            // address within the stored column length.
            if (!string.IsNullOrWhiteSpace(videoUrl))
            {
                string normalizedUrl = videoUrl.Trim();
                if (normalizedUrl.Length > LibraryContent.MaximumVideoUrlLength)
                    errors["videoUrl"] = [$"Video URL must not exceed {LibraryContent.MaximumVideoUrlLength} characters."];
                else if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    errors["videoUrl"] = ["Video URL must be an absolute http(s) URL."];
            }

            if (durationSeconds is null || durationSeconds <= 0)
                errors["durationSeconds"] = ["A positive duration is required for Video content."];

            if (effectiveSections.Count > 0)
                errors["sectionsJson"] = ["Video content cannot have article sections."];
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(videoUrl))
                errors["videoUrl"] = ["Article content cannot have a video URL."];
            if (durationSeconds is not null)
                errors["durationSeconds"] = ["Article content cannot have a duration."];

            if (effectiveSections.Count == 0)
            {
                AddError(errors, "sectionsJson", "At least one section is required for Article content.");
            }
            else
            {
                if (effectiveSections.Count > LibraryContent.MaximumSectionCount)
                    AddError(errors, "sectionsJson", $"Article content supports at most {LibraryContent.MaximumSectionCount} sections.");
                if (effectiveSections.Select(section => section.DisplayOrder).Distinct().Count() != effectiveSections.Count)
                    AddError(errors, "sectionsJson", "Section display orders must be unique.");

                for (int index = 0; index < effectiveSections.Count; index++)
                {
                    LibraryContentSectionInput section = effectiveSections[index];
                    string prefix = $"sectionsJson[{index}]";
                    if (section.DisplayOrder < 1)
                        errors[$"{prefix}.displayOrder"] = ["Display order must be a positive number."];
                    AddTextError(errors, $"{prefix}.arabicTitle", section.ArabicTitle, LibraryContentSection.MaximumTitleLength);
                    AddTextError(errors, $"{prefix}.englishTitle", section.EnglishTitle, LibraryContentSection.MaximumTitleLength);
                    AddBulletErrors(errors, $"{prefix}.arabicBullets", section.ArabicBullets);
                    AddBulletErrors(errors, $"{prefix}.englishBullets", section.EnglishBullets);
                }
            }
        }

        return errors.Count == 0 ? null : errors;
    }

    private static void AddError(Dictionary<string, string[]> errors, string field, string message)
    {
        if (errors.TryGetValue(field, out string[]? existing))
            errors[field] = [.. existing, message];
        else
            errors[field] = [message];
    }

    private static void AddTextError(Dictionary<string, string[]> errors, string field, string? value, int maximumLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
            errors[field] = [$"A value is required and must not exceed {maximumLength} characters."];
    }

    private static void AddBulletErrors(Dictionary<string, string[]> errors, string field, IReadOnlyList<string>? bullets)
    {
        IReadOnlyList<string> entries = bullets ?? [];
        if (entries.Count > LibraryContentSection.MaximumBulletCount)
        {
            errors[field] = [$"Each language supports at most {LibraryContentSection.MaximumBulletCount} bullets."];
            return;
        }

        for (int index = 0; index < entries.Count; index++)
        {
            string normalized = entries[index]?.Trim() ?? string.Empty;
            if (normalized.Length == 0 || normalized.Length > LibraryContentSection.MaximumBulletLength)
                errors[$"{field}[{index}]"] = [$"Bullet entries must be non-blank and must not exceed {LibraryContentSection.MaximumBulletLength} characters."];
        }
    }
}
