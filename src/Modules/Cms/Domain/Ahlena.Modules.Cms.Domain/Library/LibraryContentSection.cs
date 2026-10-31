using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Cms.Domain.Library;

public sealed class LibraryContentSection : Entity<Guid>
{
    public const int MaximumTitleLength = 200;
    public const int MaximumBulletCount = 30;
    public const int MaximumBulletLength = 500;

    private LibraryContentSection()
    {
    }

    private LibraryContentSection(
        Guid id,
        int displayOrder,
        string arabicTitle,
        string englishTitle,
        List<string> arabicBullets,
        List<string> englishBullets)
        : base(id)
    {
        DisplayOrder = displayOrder;
        ArabicTitle = arabicTitle;
        EnglishTitle = englishTitle;
        ArabicBullets = arabicBullets;
        EnglishBullets = englishBullets;
    }

    public int DisplayOrder { get; private set; }
    public string ArabicTitle { get; private set; } = string.Empty;
    public string EnglishTitle { get; private set; } = string.Empty;
    public List<string> ArabicBullets { get; private set; } = [];
    public List<string> EnglishBullets { get; private set; } = [];

    internal static LibraryContentSection Create(
        int displayOrder,
        string arabicTitle,
        string englishTitle,
        IReadOnlyList<string> arabicBullets,
        IReadOnlyList<string> englishBullets)
    {
        if (displayOrder < 1)
            throw new DomainException("Section display order must be a positive number.");

        return new LibraryContentSection(
            Guid.CreateVersion7(),
            displayOrder,
            RequiredText(arabicTitle, nameof(arabicTitle)),
            RequiredText(englishTitle, nameof(englishTitle)),
            NormalizeBullets(arabicBullets, nameof(arabicBullets)),
            NormalizeBullets(englishBullets, nameof(englishBullets)));
    }

    private static string RequiredText(string value, string name)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > MaximumTitleLength)
            throw new DomainException($"{name} is required and must not exceed {MaximumTitleLength} characters.");
        return normalized;
    }

    private static List<string> NormalizeBullets(IReadOnlyList<string>? bullets, string name)
    {
        if (bullets is null || bullets.Count > MaximumBulletCount)
            throw new DomainException($"{name} must contain at most {MaximumBulletCount} bullets.");

        var normalized = new List<string>(bullets.Count);
        foreach (string bullet in bullets)
        {
            string value = bullet?.Trim() ?? string.Empty;
            if (value.Length == 0 || value.Length > MaximumBulletLength)
                throw new DomainException($"{name} entries are required and must not exceed {MaximumBulletLength} characters.");
            normalized.Add(value);
        }

        return normalized;
    }
}
