using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Cms.Domain.Wellness;

public sealed class WellnessTip : AggregateRoot<Guid>
{
    public const int MaximumTitleLength = 200;
    public const int MaximumCategoryLength = 100;
    public const int MaximumImagePathLength = 500;
    public const int MaximumSectionCount = 40;
    private readonly List<WellnessTipSection> _sections = [];

    private WellnessTip() { }

    private WellnessTip(Guid id, string arTitle, string enTitle, string category, string imagePath, DateTime now)
        : base(id)
    {
        ArabicTitle = arTitle; EnglishTitle = enTitle; Category = category; ImagePath = imagePath;
        Status = WellnessTipPublicationStatus.Draft; CreatedOnUtc = now; UpdatedOnUtc = now;
    }

    public string ArabicTitle { get; private set; } = string.Empty;
    public string EnglishTitle { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public string ImagePath { get; private set; } = string.Empty;
    public WellnessTipPublicationStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? PublishedOnUtc { get; private set; }
    public IReadOnlyList<WellnessTipSection> Sections => _sections;

    public static WellnessTip Create(string arTitle, string enTitle, string category, string imagePath, IReadOnlyList<WellnessTipSectionDraft> sections)
    {
        var tip = new WellnessTip(Guid.CreateVersion7(), Normalize(arTitle, "Arabic title", MaximumTitleLength), Normalize(enTitle, "English title", MaximumTitleLength), Normalize(category, "Category", MaximumCategoryLength), Normalize(imagePath, "Image path", MaximumImagePathLength), DateTime.UtcNow);
        tip.ReplaceSections(sections); return tip;
    }

    public void Update(string arTitle, string enTitle, string category, string imagePath, IReadOnlyList<WellnessTipSectionDraft> sections)
    {
        if (Status != WellnessTipPublicationStatus.Draft) throw new DomainException("Only a draft wellness tip can be modified.");
        ArabicTitle = Normalize(arTitle, "Arabic title", MaximumTitleLength); EnglishTitle = Normalize(enTitle, "English title", MaximumTitleLength); Category = Normalize(category, "Category", MaximumCategoryLength); ImagePath = Normalize(imagePath, "Image path", MaximumImagePathLength); ReplaceSections(sections); UpdatedOnUtc = DateTime.UtcNow;
    }
    public void Publish() { if (Status == WellnessTipPublicationStatus.Archived) throw new DomainException("Archived wellness tips cannot be published."); Status = WellnessTipPublicationStatus.Published; PublishedOnUtc ??= DateTime.UtcNow; UpdatedOnUtc = DateTime.UtcNow; }
    public void Archive() { if (Status == WellnessTipPublicationStatus.Archived) return; Status = WellnessTipPublicationStatus.Archived; UpdatedOnUtc = DateTime.UtcNow; }

    private void ReplaceSections(IReadOnlyList<WellnessTipSectionDraft>? sections)
    {
        if (sections is null || sections.Count == 0 || sections.Count > MaximumSectionCount) throw new DomainException("At least one and no more than 40 sections are required.");
        if (sections.Select(x => x.DisplayOrder).Distinct().Count() != sections.Count) throw new DomainException("Section display orders must be unique.");
        _sections.Clear(); foreach (var section in sections.OrderBy(x => x.DisplayOrder)) _sections.Add(WellnessTipSection.Create(section.DisplayOrder, section.ArabicText, section.EnglishText));
    }
    private static string Normalize(string? value, string name, int max) { if (string.IsNullOrWhiteSpace(value)) throw new DomainException($"{name} is required."); var result = value.Trim(); if (result.Length > max) throw new DomainException($"{name} cannot exceed {max} characters."); return result; }
}

public sealed record WellnessTipSectionDraft(int DisplayOrder, string ArabicText, string EnglishText);
