using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Rentals.Domain;

/// <summary>
/// Whether the family has disputed a damage deduction (Owner decision #21:
/// "SuperAdmin approves documented damage deductions; customer sees
/// amount/reason and can dispute"). The dispute is recorded evidence — it
/// never rewrites or freezes the deduction record; resolution is itself
/// recorded, never a deletion.
/// </summary>
public enum RentalDamageDisputeState
{
    None = 0,
    Disputed = 1,
    Resolved = 2
}

/// <summary>
/// One documented damage deduction recorded at inspection (clause 8: "less
/// documented damage beyond normal wear"; decision #21). Append-style: the
/// record is produced once, approved at most once, disputed at most once and
/// resolved at most once. There is no edit path — corrections record new
/// rows.
/// <para>
/// Netting rules (the deposit-refund side) are decided by the booking
/// aggregate at settlement time: an approved deduction nets the deposit refund
/// only when it was approved on or before the inspection deadline (Owner
/// decision #20 — past the deadline the deposit can no longer be held).
/// </para>
/// </summary>
public sealed class RentalDamageDeduction : Entity<Guid>
{
    public const int MaximumDescriptionLength = 1000;
    public const int MaximumEvidenceReferenceLength = 300;
    public const int MaximumDisputeReasonLength = 1000;

    private RentalDamageDeduction() { }

    private RentalDamageDeduction(
        Guid id,
        Guid rentalBookingId,
        string descriptionEnglish,
        string? descriptionArabic,
        long amountMinorUnits,
        string evidenceReference,
        Guid recordedByAdminId,
        DateTime recordedOnUtc) : base(id)
    {
        RentalBookingId = rentalBookingId;
        DescriptionEnglish = descriptionEnglish;
        DescriptionArabic = descriptionArabic;
        AmountMinorUnits = amountMinorUnits;
        EvidenceReference = evidenceReference;
        RecordedByAdminId = recordedByAdminId;
        RecordedOnUtc = recordedOnUtc;
        DisputeState = RentalDamageDisputeState.None;
    }

    public Guid RentalBookingId { get; private set; }

    /// <summary>The reason the customer sees (#21). English is required; Arabic is the mirror when supplied.</summary>
    public string DescriptionEnglish { get; private set; } = string.Empty;

    public string? DescriptionArabic { get; private set; }

    /// <summary>Deduction amount, EGP piastres. Strictly positive (a zero finding is not documented damage).</summary>
    public long AmountMinorUnits { get; private set; }

    /// <summary>Reference to the stored damage documentation (photos/notes key). Mandatory (#8, #14, #21).</summary>
    public string EvidenceReference { get; private set; } = string.Empty;

    public Guid RecordedByAdminId { get; private set; }

    public DateTime RecordedOnUtc { get; private set; }

    /// <summary>SuperAdmin approval (#21). Null until approved.</summary>
    public Guid? ApprovedByAdminId { get; private set; }

    public DateTime? ApprovedOnUtc { get; private set; }

    public RentalDamageDisputeState DisputeState { get; private set; }

    public DateTime? DisputedOnUtc { get; private set; }

    public string? DisputeReason { get; private set; }

    public DateTime? DisputeResolvedOnUtc { get; private set; }

    public Guid? DisputeResolvedByAdminId { get; private set; }

    /// <summary>
    /// False while the deduction is not approved, and false for a deduction
    /// approved after the booking's inspection deadline — such late approvals
    /// are recorded evidence but never net the deposit (#20: past the deadline
    /// the deposit can no longer be held).
    /// </summary>
    public bool NetsDeposit(DateTime? inspectionDeadlineUtc)
    {
        if (ApprovedOnUtc is null)
            return false;
        if (inspectionDeadlineUtc is null)
            return false;
        if (ApprovedOnUtc.Value > inspectionDeadlineUtc.Value)
            return false;

        return true;
    }

    public static RentalDamageDeduction Create(
        Guid rentalBookingId,
        string descriptionEnglish,
        string? descriptionArabic,
        long amountMinorUnits,
        string evidenceReference,
        Guid recordedByAdminId,
        DateTime recordedOnUtc)
    {
        if (rentalBookingId == Guid.Empty)
            throw new DomainException("A damage deduction requires its booking.");
        string english = descriptionEnglish?.Trim() ?? string.Empty;
        if (english.Length == 0 || english.Length > MaximumDescriptionLength)
            throw new DomainException($"A damage deduction reason (English) must be between 1 and {MaximumDescriptionLength} characters.");
        string? arabic = string.IsNullOrWhiteSpace(descriptionArabic) ? null : descriptionArabic!.Trim();
        if (arabic is { Length: > MaximumDescriptionLength })
            throw new DomainException($"A damage deduction reason (Arabic) must not exceed {MaximumDescriptionLength} characters.");
        if (amountMinorUnits <= 0)
            throw new DomainException("A damage deduction amount must be positive.");
        string evidence = evidenceReference?.Trim() ?? string.Empty;
        if (evidence.Length == 0 || evidence.Length > MaximumEvidenceReferenceLength)
            throw new DomainException($"A damage deduction requires documented evidence (1..{MaximumEvidenceReferenceLength} characters).");
        if (recordedByAdminId == Guid.Empty)
            throw new DomainException("A damage deduction requires the recording administrator.");
        if (recordedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A damage deduction timestamp must be UTC.");

        return new(
            Guid.CreateVersion7(),
            rentalBookingId,
            english,
            arabic,
            amountMinorUnits,
            evidence,
            recordedByAdminId,
            recordedOnUtc);
    }

    /// <summary>SuperAdmin approval of the documented deduction (#21). Allowed only while the deposit is unsettled.</summary>
    public void Approve(Guid adminId, DateTime approvedOnUtc)
    {
        if (ApprovedOnUtc is not null)
            throw new DomainException("The damage deduction is already approved.");
        if (adminId == Guid.Empty)
            throw new DomainException("A damage deduction approval requires a SuperAdmin.");
        if (approvedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A damage deduction approval timestamp must be UTC.");

        ApprovedByAdminId = adminId;
        ApprovedOnUtc = approvedOnUtc;
    }

    /// <summary>Customer dispute of the amount/reason (#21). Once per deduction.</summary>
    public void RecordDispute(DateTime disputedOnUtc, string? reason)
    {
        if (DisputeState != RentalDamageDisputeState.None)
            throw new DomainException("The damage deduction is already disputed or resolved.");
        if (disputedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A damage deduction dispute timestamp must be UTC.");

        string? normalized = string.IsNullOrWhiteSpace(reason) ? null : reason!.Trim();
        if (normalized is { Length: > MaximumDisputeReasonLength })
            throw new DomainException($"A damage deduction dispute reason must not exceed {MaximumDisputeReasonLength} characters.");

        DisputeState = RentalDamageDisputeState.Disputed;
        DisputedOnUtc = disputedOnUtc;
        DisputeReason = normalized;
    }

    /// <summary>Staff resolution note for a disputed deduction. Never rewrites the dispute itself.</summary>
    public void ResolveDispute(Guid adminId, DateTime resolvedOnUtc)
    {
        if (DisputeState != RentalDamageDisputeState.Disputed)
            throw new DomainException("Only a disputed damage deduction can be marked resolved.");
        if (adminId == Guid.Empty)
            throw new DomainException("A dispute resolution requires an administrator.");
        if (resolvedOnUtc.Kind != DateTimeKind.Utc)
            throw new DomainException("A dispute resolution timestamp must be UTC.");

        DisputeState = RentalDamageDisputeState.Resolved;
        DisputeResolvedOnUtc = resolvedOnUtc;
        DisputeResolvedByAdminId = adminId;
    }
}
