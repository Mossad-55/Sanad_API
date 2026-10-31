using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.BuildingBlocks.Domain.Exceptions;

namespace Ahlena.Modules.Finance.Domain;

/// <summary>
/// Append-only record of a platform control change: actor, UTC time,
/// before/after values (state, the bilingual public maintenance messages and
/// the Card/Wallet switches), and the optional internal reason. There is no
/// public mutator and no update/delete path, so stored history cannot be
/// rewritten. The internal reason stays here and is never returned by the
/// public status API.
/// </summary>
public sealed class PlatformControlAuditEntry : Entity<Guid>
{
    public const int MaximumReasonLength = 500;

    private PlatformControlAuditEntry() { }

    private PlatformControlAuditEntry(
        Guid id,
        Guid actorUserId,
        int previousVersion,
        int newVersion,
        PlatformState beforeState,
        PlatformState afterState,
        string? beforeMaintenanceMessageArabic,
        string? afterMaintenanceMessageArabic,
        string? beforeMaintenanceMessageEnglish,
        string? afterMaintenanceMessageEnglish,
        bool beforeCardEnabled,
        bool afterCardEnabled,
        bool beforeWalletEnabled,
        bool afterWalletEnabled,
        string? internalReason,
        DateTime occurredOnUtc) : base(id)
    {
        ActorUserId = actorUserId;
        PreviousVersion = previousVersion;
        NewVersion = newVersion;
        BeforeState = beforeState;
        AfterState = afterState;
        BeforeMaintenanceMessageArabic = beforeMaintenanceMessageArabic;
        AfterMaintenanceMessageArabic = afterMaintenanceMessageArabic;
        BeforeMaintenanceMessageEnglish = beforeMaintenanceMessageEnglish;
        AfterMaintenanceMessageEnglish = afterMaintenanceMessageEnglish;
        BeforeCardEnabled = beforeCardEnabled;
        AfterCardEnabled = afterCardEnabled;
        BeforeWalletEnabled = beforeWalletEnabled;
        AfterWalletEnabled = afterWalletEnabled;
        InternalReason = internalReason;
        OccurredOnUtc = occurredOnUtc;
    }

    public Guid ActorUserId { get; private set; }
    public int PreviousVersion { get; private set; }
    public int NewVersion { get; private set; }
    public PlatformState BeforeState { get; private set; }
    public PlatformState AfterState { get; private set; }
    public string? BeforeMaintenanceMessageArabic { get; private set; }
    public string? AfterMaintenanceMessageArabic { get; private set; }
    public string? BeforeMaintenanceMessageEnglish { get; private set; }
    public string? AfterMaintenanceMessageEnglish { get; private set; }
    public bool BeforeCardEnabled { get; private set; }
    public bool AfterCardEnabled { get; private set; }
    public bool BeforeWalletEnabled { get; private set; }
    public bool AfterWalletEnabled { get; private set; }
    public string? InternalReason { get; private set; }
    public DateTime OccurredOnUtc { get; private set; }

    /// <summary>
    /// Captures one control change. Before values come from the tracked entity,
    /// after values come from the same entity once
    /// <see cref="PlatformControlState.Apply"/> has validated and normalized
    /// them, so the stored history matches exactly what was persisted.
    /// </summary>
    public static PlatformControlAuditEntry Create(
        Guid actorUserId,
        int previousVersion,
        int newVersion,
        PlatformState beforeState,
        PlatformState afterState,
        string? beforeMaintenanceMessageArabic,
        string? afterMaintenanceMessageArabic,
        string? beforeMaintenanceMessageEnglish,
        string? afterMaintenanceMessageEnglish,
        bool beforeCardEnabled,
        bool afterCardEnabled,
        bool beforeWalletEnabled,
        bool afterWalletEnabled,
        string? internalReason,
        DateTime occurredOnUtc)
    {
        if (actorUserId == Guid.Empty)
            throw new DomainException("A platform control audit entry requires an attributable actor.");
        if (previousVersion <= 0 || newVersion != previousVersion + 1)
            throw new DomainException("Platform control audit versions must advance by exactly one.");
        if (occurredOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Platform control audit timestamp must be UTC.");
        if ((internalReason?.Trim().Length ?? 0) > MaximumReasonLength)
            throw new DomainException($"Internal reason must not exceed {MaximumReasonLength} characters.");

        ValidateMessageLength(beforeMaintenanceMessageArabic);
        ValidateMessageLength(afterMaintenanceMessageArabic);
        ValidateMessageLength(beforeMaintenanceMessageEnglish);
        ValidateMessageLength(afterMaintenanceMessageEnglish);

        return new(
            Guid.CreateVersion7(),
            actorUserId,
            previousVersion,
            newVersion,
            beforeState,
            afterState,
            Normalize(beforeMaintenanceMessageArabic),
            Normalize(afterMaintenanceMessageArabic),
            Normalize(beforeMaintenanceMessageEnglish),
            Normalize(afterMaintenanceMessageEnglish),
            beforeCardEnabled,
            afterCardEnabled,
            beforeWalletEnabled,
            afterWalletEnabled,
            string.IsNullOrWhiteSpace(internalReason) ? null : internalReason.Trim(),
            occurredOnUtc);
    }

    private static void ValidateMessageLength(string? value)
    {
        if ((value?.Trim().Length ?? 0) > PlatformControlState.MaximumMessageLength)
        {
            throw new DomainException(
                $"Platform control audit messages must not exceed {PlatformControlState.MaximumMessageLength} characters.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
