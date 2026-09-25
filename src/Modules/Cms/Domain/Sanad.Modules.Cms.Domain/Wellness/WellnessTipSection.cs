using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Cms.Domain.Wellness;

public sealed class WellnessTipSection : Entity<Guid>
{
    public const int MaximumTextLength = 4000;

    private WellnessTipSection() { }

    internal WellnessTipSection(Guid id, int displayOrder, string arabicText, string englishText)
        : base(id)
    {
        DisplayOrder = displayOrder;
        ArabicText = arabicText;
        EnglishText = englishText;
    }

    public int DisplayOrder { get; private set; }
    public string ArabicText { get; private set; } = string.Empty;
    public string EnglishText { get; private set; } = string.Empty;

    internal static WellnessTipSection Create(int displayOrder, string? arabicText, string? englishText)
    {
        if (displayOrder < 1) throw new DomainException("Section display order must be a positive number.");
        return new WellnessTipSection(Guid.CreateVersion7(), displayOrder, Normalize(arabicText, "Arabic section text"), Normalize(englishText, "English section text"));
    }

    private static string Normalize(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException($"{name} is required.");
        var result = value.Trim();
        if (result.Length > MaximumTextLength) throw new DomainException($"{name} cannot exceed {MaximumTextLength} characters.");
        return result;
    }
}
