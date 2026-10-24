namespace Sanad.Modules.Families.Domain.CareRecords;

/// <summary>
/// What an append-only care-record audit row records. Authoring actions
/// (Created/Updated/Deleted) preserve deletion/edit attribution per the locked
/// D2 decision; Admin actions are the payload-free read audit required by the
/// locked D4 decision — they never carry record content.
/// </summary>
public enum CareRecordAuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
    AdminListed = 3,
    AdminDetailRead = 4
}
