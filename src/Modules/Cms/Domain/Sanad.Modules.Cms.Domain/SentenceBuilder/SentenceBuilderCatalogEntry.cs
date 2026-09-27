using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Cms.Domain.SentenceBuilder;

public enum SentenceBuilderCategory { Actor = 1, Action = 2, Need = 3, Qualifier = 4 }

public sealed class SentenceBuilderCatalogEntry : AggregateRoot<Guid>
{
    private readonly List<SentenceBuilderCatalogRevision> _revisions = [];
    private SentenceBuilderCatalogEntry() { }
    private SentenceBuilderCatalogEntry(Guid id, string key, SentenceBuilderCategory category, DateTime now) : base(id)
    { StableKey = key; Category = category; CreatedOnUtc = now; }
    public string StableKey { get; private set; } = string.Empty;
    public SentenceBuilderCategory Category { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public IReadOnlyCollection<SentenceBuilderCatalogRevision> Revisions => _revisions.AsReadOnly();
    public static SentenceBuilderCatalogEntry Create(string key, SentenceBuilderCategory category)
    { if (!Enum.IsDefined(category) || string.IsNullOrWhiteSpace(key) || key.Trim().Length > 120) throw new DomainException("A valid stable key and category are required."); return new(Guid.CreateVersion7(), key.Trim(), category, DateTime.UtcNow); }
    public SentenceBuilderCatalogRevision AddRevision(string arabicLabel, string englishLabel, int order)
    { var revision = SentenceBuilderCatalogRevision.Create(Id, _revisions.Count == 0 ? 1 : _revisions.Max(x => x.Version) + 1, arabicLabel, englishLabel, order); _revisions.Add(revision); return revision; }
}

public sealed class SentenceBuilderCatalogRevision : Entity<Guid>
{
    private SentenceBuilderCatalogRevision() { }
    private SentenceBuilderCatalogRevision(Guid id, Guid entryId, int version, string ar, string en, int order) : base(id)
    { CatalogEntryId = entryId; Version = version; ArabicLabel = ar; EnglishLabel = en; DisplayOrder = order; CreatedOnUtc = DateTime.UtcNow; }
    public Guid CatalogEntryId { get; private set; }
    public SentenceBuilderCatalogEntry? CatalogEntry { get; private set; }
    public int Version { get; private set; }
    public string ArabicLabel { get; private set; } = string.Empty;
    public string EnglishLabel { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public static SentenceBuilderCatalogRevision Create(Guid entryId, int version, string ar, string en, int order)
    { if (string.IsNullOrWhiteSpace(ar) || string.IsNullOrWhiteSpace(en) || ar.Trim().Length > 200 || en.Trim().Length > 200 || order < 0) throw new DomainException("Catalog labels and order are invalid."); return new(Guid.CreateVersion7(), entryId, version, ar.Trim(), en.Trim(), order); }
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
