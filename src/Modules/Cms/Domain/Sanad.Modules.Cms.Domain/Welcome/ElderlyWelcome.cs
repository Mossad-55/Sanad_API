using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Cms.Domain.Welcome;

public enum ElderlyWelcomePublicationStatus { Draft = 1, Published = 2, Archived = 3 }

public sealed class ElderlyWelcome : AggregateRoot<Guid>
{
    public const int MaximumTextLength = 500;
    public const int MaximumTileCount = 8;
    private readonly List<ElderlyWelcomeBenefit> _benefits = [];
    private ElderlyWelcome() { }
    private ElderlyWelcome(Guid id, string arHeadline, string enHeadline, string arCta, string enCta, DateTime now) : base(id)
    {
        ArabicHeadline = arHeadline; EnglishHeadline = enHeadline; ArabicCtaLabel = arCta; EnglishCtaLabel = enCta;
        Status = ElderlyWelcomePublicationStatus.Draft; CreatedOnUtc = UpdatedOnUtc = now;
    }
    public string ArabicHeadline { get; private set; } = string.Empty;
    public string EnglishHeadline { get; private set; } = string.Empty;
    public string ArabicCtaLabel { get; private set; } = string.Empty;
    public string EnglishCtaLabel { get; private set; } = string.Empty;
    public string CtaAction { get; private set; } = "elderly.request-otp";
    public ElderlyWelcomePublicationStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? PublishedOnUtc { get; private set; }
    public IReadOnlyList<ElderlyWelcomeBenefit> Benefits => _benefits;
    public static ElderlyWelcome Create(string arHeadline, string enHeadline, string arCta, string enCta, IReadOnlyList<ElderlyWelcomeBenefitDraft> benefits)
    {
        var welcome = new ElderlyWelcome(SingletonId, Required(arHeadline, "Arabic headline"), Required(enHeadline, "English headline"), Required(arCta, "Arabic CTA label"), Required(enCta, "English CTA label"), DateTime.UtcNow);
        welcome.ReplaceBenefits(benefits); return welcome;
    }
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public void Update(string arHeadline, string enHeadline, string arCta, string enCta, IReadOnlyList<ElderlyWelcomeBenefitDraft> benefits)
    {
        if (Status != ElderlyWelcomePublicationStatus.Draft) throw new DomainException("Only a draft Elderly welcome can be modified.");
        ArabicHeadline = Required(arHeadline, "Arabic headline"); EnglishHeadline = Required(enHeadline, "English headline");
        ArabicCtaLabel = Required(arCta, "Arabic CTA label"); EnglishCtaLabel = Required(enCta, "English CTA label");
        ReplaceBenefits(benefits); UpdatedOnUtc = DateTime.UtcNow;
    }
    public void Publish() { if (Status == ElderlyWelcomePublicationStatus.Archived) throw new DomainException("Archived Elderly welcome cannot be published."); Status = ElderlyWelcomePublicationStatus.Published; PublishedOnUtc ??= DateTime.UtcNow; UpdatedOnUtc = DateTime.UtcNow; }
    public void Unpublish()
    {
        if (Status == ElderlyWelcomePublicationStatus.Archived)
        {
            throw new DomainException("Archived Elderly welcome cannot be unpublished.");
        }

        if (Status == ElderlyWelcomePublicationStatus.Draft)
        {
            return;
        }

        Status = ElderlyWelcomePublicationStatus.Draft;
        UpdatedOnUtc = DateTime.UtcNow;
    }
    private void ReplaceBenefits(IReadOnlyList<ElderlyWelcomeBenefitDraft>? benefits)
    {
        if (benefits is null || benefits.Count == 0 || benefits.Count > MaximumTileCount) throw new DomainException("One to eight benefit tiles are required.");
        if (benefits.Select(x => x.DisplayOrder).Distinct().Count() != benefits.Count) throw new DomainException("Benefit display orders must be unique.");
        _benefits.Clear(); foreach (var b in benefits.OrderBy(x => x.DisplayOrder)) _benefits.Add(ElderlyWelcomeBenefit.Create(b.DisplayOrder, b.ArabicTitle, b.EnglishTitle, b.ArabicDescription, b.EnglishDescription));
    }
    private static string Required(string? value, string name) { if (string.IsNullOrWhiteSpace(value)) throw new DomainException($"{name} is required."); var text = value.Trim(); if (text.Length > MaximumTextLength) throw new DomainException($"{name} cannot exceed {MaximumTextLength} characters."); return text; }
}

public sealed class ElderlyWelcomeBenefit : Entity<Guid>
{
    private ElderlyWelcomeBenefit() { }
    private ElderlyWelcomeBenefit(Guid id, int displayOrder, string arTitle, string enTitle, string arDescription, string enDescription) : base(id)
    { DisplayOrder = displayOrder; ArabicTitle = arTitle; EnglishTitle = enTitle; ArabicDescription = arDescription; EnglishDescription = enDescription; }
    public int DisplayOrder { get; private set; }
    public string ArabicTitle { get; private set; } = string.Empty;
    public string EnglishTitle { get; private set; } = string.Empty;
    public string ArabicDescription { get; private set; } = string.Empty;
    public string EnglishDescription { get; private set; } = string.Empty;
    internal static ElderlyWelcomeBenefit Create(int order, string? arTitle, string? enTitle, string? arDescription, string? enDescription)
    {
        if (order < 1) throw new DomainException("Benefit display order must be positive.");
        return new(Guid.CreateVersion7(), order, Check(arTitle), Check(enTitle), Check(arDescription), Check(enDescription));
    }
    private static string Check(string? value) { if (string.IsNullOrWhiteSpace(value)) throw new DomainException("All localized benefit fields are required."); var text = value.Trim(); if (text.Length > ElderlyWelcome.MaximumTextLength) throw new DomainException($"Benefit text cannot exceed {ElderlyWelcome.MaximumTextLength} characters."); return text; }
}
public sealed record ElderlyWelcomeBenefitDraft(int DisplayOrder, string ArabicTitle, string EnglishTitle, string ArabicDescription, string EnglishDescription);
