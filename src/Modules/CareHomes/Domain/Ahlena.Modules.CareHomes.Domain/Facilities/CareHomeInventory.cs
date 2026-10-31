using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.CareHomes.Domain.Facilities;

public sealed class CareHomeRoomType : Entity<Guid>
{
    private CareHomeRoomType() { }
    private CareHomeRoomType(Guid id, CareHomeId facilityId, string ar, string en, decimal monthlyPrice, DateTime created)
        : base(id) { FacilityId = facilityId; ArabicName = ar; EnglishName = en; MonthlyPriceEgp = monthlyPrice; CreatedOnUtc = created; UpdatedOnUtc = created; }
    public CareHomeId FacilityId { get; private set; }
    public string ArabicName { get; private set; } = string.Empty;
    public string EnglishName { get; private set; } = string.Empty;
    public string? ArabicDescription { get; private set; }
    public string? EnglishDescription { get; private set; }
    public decimal MonthlyPriceEgp { get; private set; }
    public CareHomeAllocationMode AllocationMode { get; private set; }
    public IReadOnlyList<BilingualCareHomeItem> Amenities { get; private set; } = [];
    public bool IsArchived { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public static CareHomeRoomType Create(CareHomeId facilityId, string arabicName, string englishName, decimal monthlyPriceEgp, CareHomeAllocationMode allocationMode, DateTime utcNow)
    { Ensure(facilityId, arabicName, englishName, monthlyPriceEgp, allocationMode, utcNow); var result = new CareHomeRoomType(Guid.CreateVersion7(), facilityId, arabicName.Trim(), englishName.Trim(), monthlyPriceEgp, utcNow) { AllocationMode = allocationMode }; return result; }
    public void Update(string arabicName, string englishName, decimal monthlyPriceEgp, CareHomeAllocationMode allocationMode, string? arabicDescription, string? englishDescription, IReadOnlyList<BilingualCareHomeItem> amenities, DateTime utcNow)
    { Ensure(FacilityId, arabicName, englishName, monthlyPriceEgp, allocationMode, utcNow); ArabicName = arabicName.Trim(); EnglishName = englishName.Trim(); MonthlyPriceEgp = monthlyPriceEgp; AllocationMode = allocationMode; ArabicDescription = Trim(arabicDescription); EnglishDescription = Trim(englishDescription); Amenities = NormalizeAmenities(amenities); UpdatedOnUtc = utcNow; }
    // Room types are bookable only once they publish a cover photo plus at least one
    // gallery photo, mirroring the facility profile media completeness rule.
    public bool HasRequiredMedia(IReadOnlyList<CareHomeRoomTypeMedia> media) =>
        media.Any(x => x.RoomTypeId == Id && x.Kind == CareHomeRoomTypeMediaKind.Cover) &&
        media.Any(x => x.RoomTypeId == Id && x.Kind == CareHomeRoomTypeMediaKind.Gallery);
    public void Archive(DateTime utcNow) { EnsureUtc(utcNow); IsArchived = true; UpdatedOnUtc = utcNow; }
    public void Restore(DateTime utcNow) { EnsureUtc(utcNow); IsArchived = false; UpdatedOnUtc = utcNow; }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IReadOnlyList<BilingualCareHomeItem> NormalizeAmenities(IReadOnlyList<BilingualCareHomeItem> amenities)
    {
        ArgumentNullException.ThrowIfNull(amenities);
        if (amenities.Count > 50) throw new ArgumentException("Room type amenities are limited to 50 items.");
        if (amenities.Any(item => item is null)) throw new ArgumentException("Room type amenities cannot contain null entries.");
        return amenities.Select(item => new BilingualCareHomeItem(Require(item?.Arabic, 200, nameof(item.Arabic)), Require(item?.English, 200, nameof(item.English)))).ToArray();
    }
    private static string Require(string? value, int maximumLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{field} is required.");
        string trimmed = value.Trim();
        if (trimmed.Length > maximumLength) throw new ArgumentException($"{field} exceeds {maximumLength} characters.");
        return trimmed;
    }
    private static void Ensure(CareHomeId facilityId, string ar, string en, decimal price, CareHomeAllocationMode mode, DateTime utcNow) { if (facilityId == CareHomeId.Empty) throw new ArgumentException("Facility is required."); if (string.IsNullOrWhiteSpace(ar) || string.IsNullOrWhiteSpace(en)) throw new ArgumentException("Arabic and English names are required."); if (price < 0) throw new ArgumentOutOfRangeException(nameof(price)); if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode)); EnsureUtc(utcNow); }
    private static void EnsureUtc(DateTime value) { if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC."); }
}

public enum CareHomeAllocationMode { Shared = 1, Private = 2, Suite = 3 }

public enum CareHomeRoomTypeMediaKind { Cover = 1, Gallery = 2 }

public sealed class CareHomeRoomTypeMedia : Entity<Guid>
{
    private CareHomeRoomTypeMedia() { }
    private CareHomeRoomTypeMedia(Guid id, Guid roomTypeId, CareHomeRoomTypeMediaKind kind, int position, string storageKey, string contentType, long length, DateTime created) : base(id)
    { RoomTypeId = roomTypeId; Kind = kind; Position = position; StorageKey = storageKey; ContentType = contentType; Length = length; CreatedOnUtc = created; }
    public Guid RoomTypeId { get; private set; }
    public CareHomeRoomTypeMediaKind Kind { get; private set; }
    public int Position { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Length { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public void SetPosition(int position)
    {
        if (position < 0 || position > 9) throw new DomainException("Room type gallery position is invalid.");
        Position = position;
    }
    public static CareHomeRoomTypeMedia Create(Guid roomTypeId, CareHomeRoomTypeMediaKind kind, int position, string storageKey, string contentType, long length, DateTime utcNow)
    {
        if (roomTypeId == Guid.Empty || !Enum.IsDefined(kind)) throw new DomainException("Room type media target is invalid.");
        if (position < 0 || kind == CareHomeRoomTypeMediaKind.Cover && position != 0 || kind == CareHomeRoomTypeMediaKind.Gallery && position > 9) throw new DomainException("Room type media position is invalid.");
        if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith("private/care-home-media/", StringComparison.Ordinal)) throw new DomainException("Room type media must use private storage.");
        if (contentType is not ("image/jpeg" or "image/png") || length is <= 0 or > 5_242_880) throw new DomainException("Room type images must be JPG or PNG and no larger than 5 MB.");
        if (utcNow.Kind != DateTimeKind.Utc) throw new DomainException("Room type media timestamps must be UTC.");
        return new CareHomeRoomTypeMedia(Guid.CreateVersion7(), roomTypeId, kind, position, storageKey.Trim(), contentType, length, utcNow);
    }
}

public sealed class CareHomeRoom : Entity<Guid>
{
    private CareHomeRoom() { }
    private CareHomeRoom(Guid id, CareHomeId facilityId, Guid roomTypeId, string number, DateTime created) : base(id) { FacilityId = facilityId; RoomTypeId = roomTypeId; RoomNumber = number; CreatedOnUtc = created; UpdatedOnUtc = created; }
    public CareHomeId FacilityId { get; private set; }
    public Guid RoomTypeId { get; private set; }
    public string RoomNumber { get; private set; } = string.Empty;
    public string? ArabicDisplayName { get; private set; }
    public string? EnglishDisplayName { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public static CareHomeRoom Create(CareHomeId facilityId, Guid roomTypeId, string roomNumber, DateTime utcNow) { if (facilityId == CareHomeId.Empty || roomTypeId == Guid.Empty || string.IsNullOrWhiteSpace(roomNumber)) throw new ArgumentException("Facility, room type and room number are required."); EnsureUtc(utcNow); return new(Guid.CreateVersion7(), facilityId, roomTypeId, roomNumber.Trim(), utcNow); }
    public void Rename(string roomNumber, bool hasActiveOccupancy, DateTime utcNow) { if (hasActiveOccupancy) throw new InvalidOperationException("Room number cannot change while an active hold or stay exists."); if (string.IsNullOrWhiteSpace(roomNumber)) throw new ArgumentException("Room number is required."); EnsureUtc(utcNow); RoomNumber = roomNumber.Trim(); UpdatedOnUtc = utcNow; }
    public void ChangeType(Guid roomTypeId, DateTime utcNow) { if (roomTypeId == Guid.Empty) throw new ArgumentException("Room type is required."); EnsureUtc(utcNow); RoomTypeId = roomTypeId; UpdatedOnUtc = utcNow; }
    // Optional bilingual display override: blank (or whitespace) on both languages
    // clears the override so reads fall back to the room type name; the pair is
    // always set or cleared together, mirroring the bilingual amenities rule.
    public void SetDisplayName(string? arabicDisplayName, string? englishDisplayName, DateTime utcNow)
    {
        EnsureUtc(utcNow);
        string? arabic = NormalizeDisplayName(arabicDisplayName);
        string? english = NormalizeDisplayName(englishDisplayName);
        if (arabic is null && english is null)
        {
            ArabicDisplayName = null;
            EnglishDisplayName = null;
            UpdatedOnUtc = utcNow;
            return;
        }
        if (arabic is null || english is null) throw new ArgumentException("Arabic and English display names are set together or cleared together.");
        if (arabic.Length > 200) throw new ArgumentException("Arabic display name exceeds 200 characters.");
        if (english.Length > 200) throw new ArgumentException("English display name exceeds 200 characters.");
        ArabicDisplayName = arabic;
        EnglishDisplayName = english;
        UpdatedOnUtc = utcNow;
    }
    public void Archive(DateTime utcNow) { EnsureUtc(utcNow); IsArchived = true; UpdatedOnUtc = utcNow; }
    public void Restore(DateTime utcNow) { EnsureUtc(utcNow); IsArchived = false; UpdatedOnUtc = utcNow; }
    private static string? NormalizeDisplayName(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void EnsureUtc(DateTime value) { if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC."); }
}

public enum CareHomeRoomMediaKind { Cover = 1, Gallery = 2 }

// Per-room photos mirror the room type media rules byte-for-byte: one cover at
// position 0, up to ten gallery slots, private storage, 5 MB JPG/PNG cap.
public sealed class CareHomeRoomMedia : Entity<Guid>
{
    private CareHomeRoomMedia() { }
    private CareHomeRoomMedia(Guid id, Guid roomId, CareHomeRoomMediaKind kind, int position, string storageKey, string contentType, long length, DateTime created) : base(id)
    { RoomId = roomId; Kind = kind; Position = position; StorageKey = storageKey; ContentType = contentType; Length = length; CreatedOnUtc = created; }
    public Guid RoomId { get; private set; }
    public CareHomeRoomMediaKind Kind { get; private set; }
    public int Position { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long Length { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public void SetPosition(int position)
    {
        if (position < 0 || position > 9) throw new DomainException("Room gallery position is invalid.");
        Position = position;
    }
    public static CareHomeRoomMedia Create(Guid roomId, CareHomeRoomMediaKind kind, int position, string storageKey, string contentType, long length, DateTime utcNow)
    {
        if (roomId == Guid.Empty || !Enum.IsDefined(kind)) throw new DomainException("Room media target is invalid.");
        if (position < 0 || kind == CareHomeRoomMediaKind.Cover && position != 0 || kind == CareHomeRoomMediaKind.Gallery && position > 9) throw new DomainException("Room media position is invalid.");
        if (string.IsNullOrWhiteSpace(storageKey) || !storageKey.StartsWith("private/care-home-media/", StringComparison.Ordinal)) throw new DomainException("Room media must use private storage.");
        if (contentType is not ("image/jpeg" or "image/png") || length is <= 0 or > 5_242_880) throw new DomainException("Room images must be JPG or PNG and no larger than 5 MB.");
        if (utcNow.Kind != DateTimeKind.Utc) throw new DomainException("Room media timestamps must be UTC.");
        return new CareHomeRoomMedia(Guid.CreateVersion7(), roomId, kind, position, storageKey.Trim(), contentType, length, utcNow);
    }
}

public sealed class CareHomeBed : Entity<Guid>
{
    private CareHomeBed() { }
    private CareHomeBed(Guid id, CareHomeId facilityId, Guid roomId, string label, DateTime created) : base(id) { FacilityId = facilityId; RoomId = roomId; Label = label; CreatedOnUtc = created; UpdatedOnUtc = created; }
    public CareHomeId FacilityId { get; private set; }
    public Guid RoomId { get; private set; }
    public string Label { get; private set; } = string.Empty;
    public bool IsArchived { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public static CareHomeBed Create(CareHomeId facilityId, Guid roomId, string label, DateTime utcNow) { if (facilityId == CareHomeId.Empty || roomId == Guid.Empty || string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Facility, room and bed label are required."); EnsureUtc(utcNow); return new(Guid.CreateVersion7(), facilityId, roomId, label.Trim(), utcNow); }
    public void Rename(string label, DateTime utcNow) { if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("Bed label is required."); EnsureUtc(utcNow); Label = label.Trim(); UpdatedOnUtc = utcNow; }
    public void Archive(DateTime utcNow) { EnsureUtc(utcNow); IsArchived = true; UpdatedOnUtc = utcNow; }
    public void Restore(DateTime utcNow) { EnsureUtc(utcNow); IsArchived = false; UpdatedOnUtc = utcNow; }
    private static void EnsureUtc(DateTime value) { if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC."); }
}

public enum CareHomeMaintenanceTarget { Room = 1, Bed = 2 }

public sealed class CareHomeMaintenanceBlock : Entity<Guid>
{
    private CareHomeMaintenanceBlock() { }
    private CareHomeMaintenanceBlock(Guid id, CareHomeId facilityId, CareHomeMaintenanceTarget target, Guid targetId, DateOnly start, DateOnly end, string? reason, DateTime created) : base(id) { FacilityId = facilityId; Target = target; TargetId = targetId; StartDate = start; EndDate = end; Reason = reason; CreatedOnUtc = created; UpdatedOnUtc = created; }
    public CareHomeId FacilityId { get; private set; }
    public CareHomeMaintenanceTarget Target { get; private set; }
    public Guid TargetId { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public string? Reason { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? CancelledOnUtc { get; private set; }
    public Guid? CancelledBy { get; private set; }
    public static CareHomeMaintenanceBlock Create(CareHomeId facilityId, CareHomeMaintenanceTarget target, Guid targetId, DateOnly start, DateOnly end, string? reason, DateTime utcNow)
    { if (facilityId == CareHomeId.Empty || targetId == Guid.Empty || end <= start) throw new ArgumentException("A maintenance block requires a valid target and a non-empty date range."); if (!Enum.IsDefined(target)) throw new ArgumentOutOfRangeException(nameof(target)); if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC."); return new(Guid.CreateVersion7(), facilityId, target, targetId, start, end, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), utcNow); }
    public bool IsCancelled => CancelledOnUtc is not null;
    public bool Overlaps(DateOnly start, DateOnly end) => !IsCancelled && start < EndDate && end > StartDate;
    public void Amend(DateOnly start, DateOnly end, string? reason, DateTime utcNow)
    { if (end <= start) throw new ArgumentException("A maintenance block requires a valid date range."); if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC."); StartDate = start; EndDate = end; Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(); UpdatedOnUtc = utcNow; }
    public void Cancel(UserId actor, DateTime utcNow)
    { if (actor == UserId.Empty || utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("A valid cancellation actor and timestamp are required."); CancelledBy = actor.Value; CancelledOnUtc = utcNow; UpdatedOnUtc = utcNow; }
}
