namespace Sanad.Modules.Families.Domain.CareRecords;

/// <summary>
/// The locked three sources of a care record (Owner decision, 2026-10-23).
/// The numeric value is the wire value on reads (1 Family, 2 CareHome,
/// 3 Caregiver): the serializer emits this enum as its number, while the
/// bilingual display names travel separately as sourceNameAr/sourceNameEn.
/// A read filter accepts either the number or the enum name.
/// </summary>
public enum CareRecordSource
{
    Family = 1,
    CareHome = 2,
    Caregiver = 3
}
