using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Families.Application.CareRecords;

/// <summary>
/// Stable care-record error codes (W1 slice). Their HTTP status mappings are
/// integrated in the shared ResultProblemDetailsMapper; the exact shared
/// integration is recorded in the W1 care-records contract under
/// docs/internal/work/portal-api-reconciliation/contracts/.
/// <para>
/// Precedence, pinned for the whole slice: a malformed request (bad filters or
/// fields) is 400 before authorization is even attempted; authorization
/// failure for a dependent the caller cannot prove any relation to, or a
/// source the caller may never write, is 403; a record that exists but sits
/// outside the authorized scope renders as the scoped 404; a stale version is
/// 409 regardless of role.
/// </para>
/// </summary>
public static class CareRecordErrors
{
    /// <summary>403. Wrong role, wrong source dimension or expired access window.</summary>
    public static readonly Error AccessDenied =
        new("Families.CareRecords.AccessDenied",
            "You are not allowed to access this care record.");

    /// <summary>404. The dependent does not exist or is outside every authorized scope.</summary>
    public static readonly Error DependentNotFound =
        new("Families.CareRecords.DependentNotFound",
            "The dependent was not found.");

    /// <summary>404. The record exists as a known id but is not part of the authorized scope.</summary>
    public static readonly Error NotFound =
        new("Families.CareRecords.NotFound",
            "The care record was not found.");

    /// <summary>400. Required fields, lengths or plain-text rules violated.</summary>
    public static readonly Error Invalid =
        new("Families.CareRecords.Invalid",
            "The care-record details are invalid.");

    /// <summary>400. Bad paging, unknown source value or reversed filters.</summary>
    public static readonly Error InvalidQuery =
        new("Families.CareRecords.InvalidQuery",
            "The care-record query is invalid.");

    /// <summary>409. The record changed (or vanished) under a stale version.</summary>
    public static readonly Error ConcurrencyConflict =
        new("Families.CareRecords.ConcurrencyConflict",
            "The care record was changed by someone else; reload and retry.");
}
