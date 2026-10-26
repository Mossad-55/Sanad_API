using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Families.Domain.CareRecords;

namespace Sanad.Modules.Families.Application.CareRecords;

// ============================== Wire contracts ==============================

/// <summary>
/// One timeline item. `source` is the enum's numeric wire value (1 Family,
/// 2 CareHome, 3 Caregiver) — the serializer emits the number, not the name.
/// `sourceNameAr`/`sourceNameEn` carry the bilingual display names and are
/// display/localization only, never authorization keys. `version` is the
/// optimistic-concurrency token an editor echoes back as expectedVersion.
/// </summary>
public sealed record CareRecordResponse(
    Guid Id,
    Guid DependentId,
    CareRecordSource Source,
    string SourceNameAr,
    string SourceNameEn,
    Guid AuthorUserId,
    string HospitalName,
    DateOnly VisitDate,
    string Details,
    DateTime CreatedOnUtc,
    DateTime? UpdatedOnUtc,
    int Version);

/// <summary>Locked paged envelope shape: { items, page, pageSize, totalCount, totalPages }.</summary>
public sealed record CareRecordsPage<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>One picker row — names only, never medical data.</summary>
public sealed record CareRecordRecipientResponse(
    Guid DependentId,
    string ArabicFullName,
    string EnglishFullName);

/// <summary>Locked paging defaults: page 1, pageSize 20, maximum 100.</summary>
public static class CareRecordPageSettings
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Trimmed search text limit; over it the query is rejected.</summary>
    public const int MaxSearchLength = 100;
}

/// <summary>
/// Normalized paging + optional search. Use <see cref="Normalize"/> in every
/// list handler so the paging/search rules stay identical across portals.
/// </summary>
public sealed record CareRecordPageQuery(string? Search, int Page, int PageSize)
{
    public string? NormalizedSearch { get; private set; }

    /// <summary>Applies the locked paging/search validation; null on success.</summary>
    public Error? Normalize()
    {
        if (Page < 1 || PageSize < 1 || PageSize > CareRecordPageSettings.MaxPageSize)
        {
            return CareRecordErrors.InvalidQuery;
        }

        if (string.IsNullOrWhiteSpace(Search))
        {
            NormalizedSearch = null;
            return null;
        }

        string trimmed = Search.Trim();

        if (trimmed.Length > CareRecordPageSettings.MaxSearchLength)
        {
            return CareRecordErrors.InvalidQuery;
        }

        NormalizedSearch = trimmed.ToLowerInvariant();
        return null;
    }

    public static CareRecordsPage<T> CreatePage<T>(
        IReadOnlyList<T> items,
        int page,
        int pageSize,
        int totalCount) =>
        new(
            items,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize));
}

/// <summary>
/// Shared timeline filter validation: inclusive date-only visitDate range and
/// a defined source. Reversed ranges and unknown source values are rejected
/// (400) before any data is touched. Filters can only narrow a scope, never
/// widen one.
/// </summary>
public static class CareRecordFilterValidation
{
    public static Error? Validate(CareRecordSource? source, DateOnly? from, DateOnly? to)
    {
        if (source.HasValue && !Enum.IsDefined(source.Value))
        {
            return CareRecordErrors.InvalidQuery;
        }

        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            return CareRecordErrors.InvalidQuery;
        }

        return null;
    }

    public static IQueryable<CareRecord> Apply(
        this IQueryable<CareRecord> query,
        CareRecordSource? source,
        DateOnly? from,
        DateOnly? to,
        string? normalizedSearch)
    {
        if (source.HasValue)
        {
            query = query.Where(r => r.Source == source.Value);
        }

        if (from.HasValue)
        {
            query = query.Where(r => r.VisitDate >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(r => r.VisitDate <= to.Value);
        }

        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            // Pinned search policy: hospitalName (and picker names) only.
            // Medical details are never full-text searched.
            query = query.Where(r => r.HospitalName.ToLower().Contains(normalizedSearch));
        }

        return query;
    }

    /// <summary>
    /// The deterministic locked ordering: visitDate DESC, createdOnUtc DESC,
    /// id DESC. Stable ties do not imply a frozen snapshot across concurrent
    /// insertions.
    /// </summary>
    public static IOrderedQueryable<CareRecord> OrderNewestFirst(this IQueryable<CareRecord> query) =>
        query
            .OrderByDescending(r => r.VisitDate)
            .ThenByDescending(r => r.CreatedOnUtc)
            .ThenByDescending(r => r.Id);
}

public static class CareRecordMappings
{
    public static CareRecordResponse ToResponse(this CareRecord record) =>
        new(
            record.Id,
            record.ElderlyId.Value,
            record.Source,
            GetSourceNameAr(record.Source),
            GetSourceNameEn(record.Source),
            record.AuthorUserId.Value,
            record.HospitalName,
            record.VisitDate,
            record.Details,
            record.CreatedOnUtc,
            record.UpdatedOnUtc,
            record.Version);

    public static string GetSourceNameAr(CareRecordSource source) => source switch
    {
        CareRecordSource.Family => "العائلة",
        CareRecordSource.CareHome => "دار الرعاية",
        CareRecordSource.Caregiver => "مقدم الرعاية",
        _ => "غير معروف"
    };

    public static string GetSourceNameEn(CareRecordSource source) => source switch
    {
        CareRecordSource.Family => "Family",
        CareRecordSource.CareHome => "Care Home",
        CareRecordSource.Caregiver => "Caregiver",
        _ => "Unknown"
    };
}
