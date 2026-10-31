using Ahlena.Modules.Cms.Domain.Library;

namespace Ahlena.Modules.Cms.Application.Library;

/// <summary>
/// Field-level detail for a library content <c>DomainException</c>. The keys
/// mirror <c>LibraryContentValidation.Validate</c> in the API layer
/// (<c>arabicTitle</c>, <c>englishTitle</c>, <c>sectionsJson[N].arabicTitle</c>,
/// …) so the handler-produced <c>Cms.LibraryContent.Invalid</c> now carries the
/// same field paths as the controller-produced one. The stable error code is
/// unchanged; only an optional <c>errors</c> map is added.
/// <para>
/// The domain throws fixed, safe messages (no request data, no parser or
/// database text), so the message itself can be translated back to its field
/// path. Section-indexed failures are disambiguated against the request's
/// sections using the same ordering the domain applies.
/// </para>
/// </summary>
internal static class LibraryContentFieldDetail
{
    /// <summary>
    /// Builds the field errors for a caught <see cref="Ahlena.BuildingBlocks.Domain.Exceptions.DomainException"/>.
    /// Always returns a non-empty map: an unrecognized message falls back to a
    /// generic content key so the caller never emits an empty <c>errors</c> map.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> FromDomainException(
        string message,
        LibraryContentType type,
        IReadOnlyList<LibraryContentSectionInput>? sections)
    {
        string field = ResolveField(message, type, sections);
        return new Dictionary<string, string[]>
        {
            [field] = [message]
        };
    }

    private static string ResolveField(
        string message,
        LibraryContentType type,
        IReadOnlyList<LibraryContentSectionInput>? sections)
    {
        switch (message)
        {
            case "Library content type is invalid.":
                return "type";

            case "Video URL cannot exceed 500 characters.":
            case "Video URL must be an absolute http(s) URL.":
            case "Article content cannot have a video URL.":
                return "videoUrl";

            case "A stored video reference is required.":
            case "Stored video reference is invalid.":
            case "Article content cannot have a video file.":
            case "Only video content can have a video file.":
                return "videoStorageKey";

            case "Video content requires a positive duration in seconds.":
            case "Article content cannot have a duration.":
                return "durationSeconds";

            case "Video content cannot have article sections.":
            case "Article content requires at least one section.":
            case "Article content supports at most 40 sections.":
            case "Section display orders must be unique.":
                return "sectionsJson";
        }

        // "{name} is required and must not exceed {n} characters." is thrown by
        // both the top-level content text and a section's own title. The two
        // are distinguishable from the request: a section only fails after the
        // top-level text has already passed, so a matching section owns the
        // message; otherwise the top-level field does.
        if (IsRequiredText(message, out string? namedText))
        {
            int matchedIndex = FindSectionIndex(message, type, sections);
            if (matchedIndex >= 0)
                return $"sectionsJson[{matchedIndex}].{namedText}";

            if (namedText is "arabicTitle" or "englishTitle" or "arabicSummary" or "englishSummary" or "coverImagePath")
                return namedText;
        }

        // Section-level failures carry the section's own field name; the index
        // is resolved from the request using the domain's ordering.
        int index = ResolveSectionIndex(message, type, sections);

        if (message == "Section display order must be a positive number.")
            return $"sectionsJson[{index}].displayOrder";

        if (TryMatchNamedField(message, " must contain at most 30 bullets.", out string? bulletName))
            return $"sectionsJson[{index}].{bulletName}";

        if (TryMatchNamedField(message, " entries are required and must not exceed 500 characters.", out string? entryName))
            return $"sectionsJson[{index}].{entryName}";

        // Unrecognized domain text: keep the stable code and attach the safe
        // message under the content-level key rather than inventing a field.
        return "content";
    }

    private static int ResolveSectionIndex(
        string message,
        LibraryContentType type,
        IReadOnlyList<LibraryContentSectionInput>? sections)
    {
        int index = FindSectionIndex(message, type, sections);
        return index >= 0 ? index : 0;
    }

    /// <summary>
    /// The index of the section the message belongs to, or <c>-1</c> when no
    /// section in the request matches the failure (the message is then a
    /// top-level content failure).
    /// </summary>
    private static int FindSectionIndex(
        string message,
        LibraryContentType type,
        IReadOnlyList<LibraryContentSectionInput>? sections)
    {
        if (sections is null || sections.Count == 0)
            return -1;

        // The domain orders sections by display order before creating them, so
        // the failing index is an index into the ordered list.
        List<LibraryContentSectionInput> ordered = type == LibraryContentType.Article
            ? sections.OrderBy(section => section.DisplayOrder).ToList()
            : sections.ToList();

        for (int index = 0; index < ordered.Count; index++)
        {
            if (SectionMatches(ordered[index], message))
                return index;
        }

        return -1;
    }

    private static bool SectionMatches(LibraryContentSectionInput section, string message)
    {
        if (message == "Section display order must be a positive number.")
            return section.DisplayOrder < 1;

        if (IsRequiredText(message, out string? name))
        {
            return name switch
            {
                "arabicTitle" => IsBlankOrOverlong(section.ArabicTitle, LibraryContentSection.MaximumTitleLength),
                "englishTitle" => IsBlankOrOverlong(section.EnglishTitle, LibraryContentSection.MaximumTitleLength),
                _ => false
            };
        }

        if (TryMatchNamedField(message, " must contain at most 30 bullets.", out string? bulletName))
        {
            return bulletName switch
            {
                "arabicBullets" => (section.ArabicBullets?.Count ?? 0) > LibraryContentSection.MaximumBulletCount,
                "englishBullets" => (section.EnglishBullets?.Count ?? 0) > LibraryContentSection.MaximumBulletCount,
                _ => false
            };
        }

        if (TryMatchNamedField(message, " entries are required and must not exceed 500 characters.", out string? entryName))
        {
            IReadOnlyList<string>? bullets = entryName switch
            {
                "arabicBullets" => section.ArabicBullets,
                "englishBullets" => section.EnglishBullets,
                _ => null
            };

            return bullets is not null &&
                bullets.Any(bullet => IsBlankOrOverlong(bullet, LibraryContentSection.MaximumBulletLength));
        }

        return false;
    }

    private static bool IsRequiredText(string message, out string? name)
    {
        name = null;
        const string suffix = " is required and must not exceed ";

        int marker = message.IndexOf(suffix, StringComparison.Ordinal);
        if (marker <= 0 || !message.EndsWith(" characters.", StringComparison.Ordinal))
            return false;

        name = message[..marker];
        return name.Length > 0;
    }

    private static bool TryMatchNamedField(string message, string suffix, out string? name)
    {
        name = null;
        if (!message.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        name = message[..^suffix.Length];
        return name.Length > 0;
    }

    private static bool IsBlankOrOverlong(string? value, int maximumLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length == 0 || normalized.Length > maximumLength;
    }
}
