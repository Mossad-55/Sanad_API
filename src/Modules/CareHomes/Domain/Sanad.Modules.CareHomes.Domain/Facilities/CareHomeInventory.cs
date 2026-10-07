using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.CareHomes.Domain.Facilities;

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
    public bool IsArchived { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public static CareHomeRoomType Create(CareHomeId facilityId, string arabicName, string englishName, decimal monthlyPriceEgp, CareHomeAllocationMode allocationMode, DateTime utcNow)
    { Ensure(facilityId, arabicName, englishName, monthlyPriceEgp, allocationMode, utcNow); var result = new CareHomeRoomType(Guid.CreateVersion7(), facilityId, arabicName.Trim(), englishName.Trim(), monthlyPriceEgp, utcNow) { AllocationMode = allocationMode }; return result; }
    public void Update(string arabicName, string englishName, decimal monthlyPriceEgp, CareHomeAllocationMode allocationMode, string? arabicDescription, string? englishDescription, DateTime utcNow)
    { Ensure(FacilityId, arabicName, englishName, monthlyPriceEgp, allocationMode, utcNow); ArabicName = arabicName.Trim(); EnglishName = englishName.Trim(); MonthlyPriceEgp = monthlyPriceEgp; AllocationMode = allocationMode; ArabicDescription = Trim(arabicDescription); EnglishDescription = Trim(englishDescription); UpdatedOnUtc = utcNow; }
    public void Archive(DateTime utcNow) { EnsureUtc(utcNow); IsArchived = true; UpdatedOnUtc = utcNow; }
    public void Restore(DateTime utcNow) { EnsureUtc(utcNow); IsArchived = false; UpdatedOnUtc = utcNow; }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Ensure(CareHomeId facilityId, string ar, string en, decimal price, CareHomeAllocationMode mode, DateTime utcNow) { if (facilityId == CareHomeId.Empty) throw new ArgumentException("Facility is required."); if (string.IsNullOrWhiteSpace(ar) || string.IsNullOrWhiteSpace(en)) throw new ArgumentException("Arabic and English names are required."); if (price < 0) throw new ArgumentOutOfRangeException(nameof(price)); if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode)); EnsureUtc(utcNow); }
    private static void EnsureUtc(DateTime value) { if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC."); }
}

public enum CareHomeAllocationMode { Shared = 1, Private = 2, Suite = 3 }

public sealed class CareHomeRoom : Entity<Guid>
{
    private CareHomeRoom() { }
    private CareHomeRoom(Guid id, CareHomeId facilityId, Guid roomTypeId, string number, DateTime created) : base(id) { FacilityId = facilityId; RoomTypeId = roomTypeId; RoomNumber = number; CreatedOnUtc = created; UpdatedOnUtc = created; }
    public CareHomeId FacilityId { get; private set; }
    public Guid RoomTypeId { get; private set; }
    public string RoomNumber { get; private set; } = string.Empty;
    public bool IsArchived { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public static CareHomeRoom Create(CareHomeId facilityId, Guid roomTypeId, string roomNumber, DateTime utcNow) { if (facilityId == CareHomeId.Empty || roomTypeId == Guid.Empty || string.IsNullOrWhiteSpace(roomNumber)) throw new ArgumentException("Facility, room type and room number are required."); EnsureUtc(utcNow); return new(Guid.CreateVersion7(), facilityId, roomTypeId, roomNumber.Trim(), utcNow); }
    public void Rename(string roomNumber, bool hasActiveOccupancy, DateTime utcNow) { if (hasActiveOccupancy) throw new InvalidOperationException("Room number cannot change while an active hold or stay exists."); if (string.IsNullOrWhiteSpace(roomNumber)) throw new ArgumentException("Room number is required."); EnsureUtc(utcNow); RoomNumber = roomNumber.Trim(); UpdatedOnUtc = utcNow; }
    public void ChangeType(Guid roomTypeId, DateTime utcNow) { if (roomTypeId == Guid.Empty) throw new ArgumentException("Room type is required."); EnsureUtc(utcNow); RoomTypeId = roomTypeId; UpdatedOnUtc = utcNow; }
    public void Archive(DateTime utcNow) { EnsureUtc(utcNow); IsArchived = true; UpdatedOnUtc = utcNow; }
    public void Restore(DateTime utcNow) { EnsureUtc(utcNow); IsArchived = false; UpdatedOnUtc = utcNow; }
    private static void EnsureUtc(DateTime value) { if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC."); }
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
