using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Application.Abstractions.Caregivers;

public interface IMedicalCaregiverGateway
{
    Task<bool> IsActiveMedicalCaregiverAsync(
        UserId userId,
        CancellationToken cancellationToken = default);

    Task<MedicalCaregiverRecipientPage> SearchActiveMedicalCaregiversAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batched caregiverId → display-name lookup for admin booking reads. Routing data only:
    /// the caregiver id is never display output. Empty result members (or absent ids) mean
    /// "no display name is published" and surface to callers as null — never as invented
    /// identity. The batch must be a fixed small number of reads per call (caregiver page +
    /// identity directory), not per-item lookups.
    /// </summary>
    /// <remarks>
    /// The default body keeps existing stub/implementing test doubles compiling after this
    /// member was added; production fusion is provided by the API integration adapter. A
    /// default of "no names" is the same reading a caller gets for a missing identity.
    /// </remarks>
    Task<IReadOnlyDictionary<Guid, CaregiverDisplayName>> GetCaregiverDisplayNamesAsync(
        IReadOnlyCollection<Guid> caregiverIds,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<Guid, CaregiverDisplayName>>(
            new Dictionary<Guid, CaregiverDisplayName>());
}

/// <summary>
/// Display names for one caregiver, or nulls when no safe display identity exists (the
/// caregiver row is missing, or the underlying identity account is anonymized and Identity's
/// display-info query deliberately withholds it). Callers must render nulls as missing
/// names — never fall back to ids or hidden personal data.
/// </summary>
public sealed record CaregiverDisplayName(
    Guid CaregiverId,
    string? ArabicFullName,
    string? EnglishFullName);
