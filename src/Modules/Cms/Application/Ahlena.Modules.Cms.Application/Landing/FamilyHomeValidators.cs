using FluentValidation;
using Ahlena.Modules.Cms.Domain.Landing;

namespace Ahlena.Modules.Cms.Application.Landing;

/// <summary>
/// The one place family-home content rules live. Handlers call these rules and
/// map a non-null gap to a published <c>Cms.FamilyHome.*</c> code, so the
/// rules are unit-testable through the handlers and every rejection carries a
/// stable business code instead of an opaque validation body.
///
/// Locked rules:
/// - caps: title/label 200, body 4000, image/icon key 500;
/// - optional copy (title pair, body pair) is supplied in both languages or
///   omitted in both;
/// - a draft holds at most 10 items; a published section holds 1-10 items;
/// - banners and quick links never carry a catalog service id, featured
///   entries never carry editorial overrides;
/// - featured service ids cannot repeat inside one section;
/// - navigation targets are exactly <c>services</c> (no service id) or
///   <c>service</c> (one non-empty service id), compared ordinally.
/// </summary>
internal static class FamilyHomeContentRules
{
    public const int MaximumItems = 10;
    public const int MinimumPublishedItems = 1;

    public const string ServicesTarget = "services";
    public const string ServiceTarget = "service";

    /// <summary>
    /// Shape validation for any create/update payload. Returns null when the
    /// content is well formed; drafts may be empty.
    /// </summary>
    public static string? ValidateContent(
        FamilyHomeSectionKind kind,
        IReadOnlyList<FamilyHomeItem> items)
    {
        if (items.Count > MaximumItems)
            return $"a section holds at most {MaximumItems} items.";

        var featuredServiceIds = new HashSet<Guid>();

        for (int index = 0; index < items.Count; index++)
        {
            FamilyHomeItem item = items[index];
            string position = $"item {index + 1}";

            if (kind == FamilyHomeSectionKind.FeaturedServices)
            {
                if (item.ServiceId is null || item.ServiceId == Guid.Empty)
                    return $"{position} is a featured entry without a catalog service id.";

                if (item.Target is not null ||
                    item.ArabicTitle is not null ||
                    item.EnglishTitle is not null ||
                    item.ArabicBody is not null ||
                    item.EnglishBody is not null ||
                    item.ImageKey is not null ||
                    item.IconKey is not null)
                {
                    return $"{position} is a featured entry with an editorial override; " +
                        "featured entries use the live catalog name, icon and type only.";
                }

                if (!featuredServiceIds.Add(item.ServiceId.Value))
                    return $"featured service ids cannot repeat within a section ({position}).";

                continue;
            }

            if (item.ServiceId is not null)
                return $"{position} carries a catalog service id, which only featured entries allow.";

            string? capGap = ValidateEditorialCaps(item, position);
            if (capGap is not null)
                return capGap;

            if (item.Target is not null)
            {
                string? targetGap = ValidateTarget(item.Target, position);
                if (targetGap is not null)
                    return targetGap;
            }
        }

        return null;
    }

    /// <summary>
    /// Publication completeness for a draft. Content shape is checked first, so
    /// this only reports what a published section must additionally carry.
    /// </summary>
    public static string? ValidatePublishedContent(
        FamilyHomeSectionKind kind,
        IReadOnlyList<FamilyHomeItem> items)
    {
        if (items.Count < MinimumPublishedItems)
            return $"a published section requires at least {MinimumPublishedItems} item.";

        if (items.Count > MaximumItems)
            return $"a section holds at most {MaximumItems} items.";

        for (int index = 0; index < items.Count; index++)
        {
            FamilyHomeItem item = items[index];
            string position = $"item {index + 1}";

            switch (kind)
            {
                case FamilyHomeSectionKind.Banners:
                    if (item.ArabicTitle is null || item.EnglishTitle is null)
                        return $"{position} is a banner without an Arabic and English title.";
                    if (item.ImageKey is null)
                        return $"{position} is a banner without an image key.";
                    break;

                case FamilyHomeSectionKind.QuickLinks:
                    if (item.ArabicTitle is null || item.EnglishTitle is null)
                        return $"{position} is a quick link without an Arabic and English label.";
                    if (item.Target is null)
                        return $"{position} is a quick link without a navigation target.";
                    break;

                case FamilyHomeSectionKind.FeaturedServices:
                    if (item.ServiceId is null || item.ServiceId == Guid.Empty)
                        return $"{position} is a featured entry without a catalog service id.";
                    break;

                default:
                    return "the section kind is not supported.";
            }
        }

        return null;
    }

    /// <summary>Catalog service ids referenced by featured entries, in order.</summary>
    public static IReadOnlyList<Guid> FeaturedServiceIds(IReadOnlyList<FamilyHomeItem> items) =>
        items
            .Where(item => item.ServiceId is not null && item.ServiceId != Guid.Empty)
            .Select(item => item.ServiceId!.Value)
            .Distinct()
            .ToList();

    /// <summary>Catalog service ids referenced by a <c>service</c> navigation target.</summary>
    public static IReadOnlyList<Guid> TargetServiceIds(IReadOnlyList<FamilyHomeItem> items) =>
        items
            .Where(item => item.Target is not null &&
                string.Equals(item.Target.Type, ServiceTarget, StringComparison.Ordinal) &&
                item.Target.ServiceId is not null &&
                item.Target.ServiceId != Guid.Empty)
            .Select(item => item.Target!.ServiceId!.Value)
            .Distinct()
            .ToList();

    private static string? ValidateEditorialCaps(FamilyHomeItem item, string position)
    {
        string? titleGap = ValidatePair(
            item.ArabicTitle,
            item.EnglishTitle,
            FamilyHomeSection.MaximumTitleLength,
            "title",
            position);
        if (titleGap is not null)
            return titleGap;

        string? bodyGap = ValidatePair(
            item.ArabicBody,
            item.EnglishBody,
            FamilyHomeSection.MaximumBodyLength,
            "body",
            position);
        if (bodyGap is not null)
            return bodyGap;

        if (item.ImageKey is not null && item.ImageKey.Length > FamilyHomeSection.MaximumKeyLength)
            return $"{position} image key cannot exceed {FamilyHomeSection.MaximumKeyLength} characters.";

        if (item.IconKey is not null && item.IconKey.Length > FamilyHomeSection.MaximumKeyLength)
            return $"{position} icon key cannot exceed {FamilyHomeSection.MaximumKeyLength} characters.";

        return null;
    }

    private static string? ValidatePair(
        string? arabic,
        string? english,
        int maximumLength,
        string field,
        string position)
    {
        if ((arabic is null) != (english is null))
            return $"{position} {field} must be supplied in both languages or omitted in both.";

        if (arabic is not null && arabic.Length > maximumLength)
            return $"{position} {field} cannot exceed {maximumLength} characters.";

        if (english is not null && english.Length > maximumLength)
            return $"{position} {field} cannot exceed {maximumLength} characters.";

        return null;
    }

    private static string? ValidateTarget(FamilyHomeTarget target, string position)
    {
        if (string.Equals(target.Type, ServicesTarget, StringComparison.Ordinal))
        {
            return target.ServiceId is null || target.ServiceId == Guid.Empty
                ? null
                : $"{position} uses the '{ServicesTarget}' target, which cannot carry a service id.";
        }

        if (string.Equals(target.Type, ServiceTarget, StringComparison.Ordinal))
        {
            return target.ServiceId is null || target.ServiceId == Guid.Empty
                ? $"{position} uses the '{ServiceTarget}' target without a service id."
                : null;
        }

        return $"{position} uses the unsupported navigation target '{target.Type}'; " +
            $"only '{ServicesTarget}' and '{ServiceTarget}' exist.";
    }
}

public sealed class CreateFamilyHomeSectionCommandValidator :
    AbstractValidator<CreateFamilyHomeSectionCommand>
{
    public CreateFamilyHomeSectionCommandValidator()
    {
        // Shape only. Every content rule is enforced by FamilyHomeContentRules
        // inside the handler so the rejection carries a Cms.FamilyHome.* code.
        RuleFor(command => command.Kind).IsInEnum();
        RuleFor(command => command.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateFamilyHomeSectionCommandValidator :
    AbstractValidator<UpdateFamilyHomeSectionCommand>
{
    public UpdateFamilyHomeSectionCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.DisplayOrder).GreaterThanOrEqualTo(0);
    }
}
