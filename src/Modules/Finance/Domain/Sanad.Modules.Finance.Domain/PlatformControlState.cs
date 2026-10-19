using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Finance.Domain;

/// <summary>
/// Platform business availability. Only these two states exist; there is no
/// default that silently resolves an unreadable control record to
/// <see cref="Operational"/>.
/// </summary>
public enum PlatformState
{
    Operational = 1,
    Maintenance = 2
}

/// <summary>
/// Payment methods the platform can currently support. Deliberately excludes
/// the provider's unimplemented ApplePay choice so E04 never advertises it.
/// </summary>
public enum PlatformPaymentMethod
{
    Card = 1,
    Wallet = 2
}

/// <summary>
/// The single database-backed platform control record: current operational
/// state, the approved bilingual public maintenance message, and the Card/
/// Wallet switches. <see cref="Version"/> is the optimistic concurrency token;
/// stale admin updates cannot overwrite newer state.
/// </summary>
public sealed class PlatformControlState : Entity<Guid>
{
    public const int MaximumMessageLength = 500;

    private PlatformControlState() { }

    private PlatformControlState(
        Guid id,
        PlatformState state,
        string? maintenanceMessageArabic,
        string? maintenanceMessageEnglish,
        bool cardEnabled,
        bool walletEnabled,
        int version,
        DateTime updatedOnUtc) : base(id)
    {
        State = state;
        MaintenanceMessageArabic = maintenanceMessageArabic;
        MaintenanceMessageEnglish = maintenanceMessageEnglish;
        CardEnabled = cardEnabled;
        WalletEnabled = walletEnabled;
        Version = version;
        UpdatedOnUtc = updatedOnUtc;
    }

    public PlatformState State { get; private set; }
    public string? MaintenanceMessageArabic { get; private set; }
    public string? MaintenanceMessageEnglish { get; private set; }
    public bool CardEnabled { get; private set; }
    public bool WalletEnabled { get; private set; }
    public int Version { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }

    /// <summary>
    /// Creates the initial control record. The E04 migration seeds exactly one
    /// deterministic Operational row through <c>InsertData</c>; this factory is
    /// the equivalent boundary for synthetic fixtures.
    /// </summary>
    public static PlatformControlState Create(
        Guid id,
        PlatformState state,
        string? maintenanceMessageArabic,
        string? maintenanceMessageEnglish,
        bool cardEnabled,
        bool walletEnabled,
        int version,
        DateTime updatedOnUtc)
    {
        Validate(state, maintenanceMessageArabic, maintenanceMessageEnglish, updatedOnUtc);
        if (version <= 0) throw new DomainException("Platform control version must be positive.");
        if (id == Guid.Empty) throw new DomainException("Platform control id is required.");

        return new(
            id,
            state,
            Normalize(maintenanceMessageArabic),
            Normalize(maintenanceMessageEnglish),
            cardEnabled,
            walletEnabled,
            version,
            updatedOnUtc);
    }

    /// <summary>
    /// Applies an approved control change and advances the concurrency version.
    /// Validation happens before any mutation, so a rejected update leaves the
    /// tracked entity untouched.
    /// </summary>
    public void Apply(
        PlatformState state,
        string? maintenanceMessageArabic,
        string? maintenanceMessageEnglish,
        bool cardEnabled,
        bool walletEnabled,
        DateTime updatedOnUtc)
    {
        Validate(state, maintenanceMessageArabic, maintenanceMessageEnglish, updatedOnUtc);

        State = state;
        MaintenanceMessageArabic = Normalize(maintenanceMessageArabic);
        MaintenanceMessageEnglish = Normalize(maintenanceMessageEnglish);
        CardEnabled = cardEnabled;
        WalletEnabled = walletEnabled;
        Version++;
        UpdatedOnUtc = updatedOnUtc;
    }

    private static void Validate(
        PlatformState state,
        string? maintenanceMessageArabic,
        string? maintenanceMessageEnglish,
        DateTime updatedOnUtc)
    {
        if (state is not (PlatformState.Operational or PlatformState.Maintenance))
            throw new DomainException("Platform state must be Operational or Maintenance.");
        if (updatedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("Platform control timestamp must be UTC.");
        if ((maintenanceMessageArabic?.Trim().Length ?? 0) > MaximumMessageLength)
            throw new DomainException($"Arabic maintenance message must not exceed {MaximumMessageLength} characters.");
        if ((maintenanceMessageEnglish?.Trim().Length ?? 0) > MaximumMessageLength)
            throw new DomainException($"English maintenance message must not exceed {MaximumMessageLength} characters.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
