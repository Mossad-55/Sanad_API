namespace Sanad.Modules.Families.Domain.CareRecords;

/// <summary>
/// The locked three sources of a care record (Owner decision, 2026-10-23).
/// The wire value is the enum NAME (Family | CareHome | Caregiver) — never the
/// numeric value, which the API does not expose.
/// </summary>
public enum CareRecordSource
{
    Family = 1,
    CareHome = 2,
    Caregiver = 3
}
