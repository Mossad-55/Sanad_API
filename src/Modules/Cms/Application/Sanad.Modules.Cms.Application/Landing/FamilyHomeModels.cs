using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Cms.Application.Landing;

/// <summary>
/// Navigation target stored inside a banner action or a quick link. Only the
/// two allowlisted tokens exist: <c>services</c> (the whole catalog, no
/// service id) and <c>service</c> (one catalog service id). There is no
/// arbitrary URL, route or extra parameter.
/// </summary>
public sealed record FamilyHomeTarget(string Type, Guid? ServiceId);

/// <summary>
/// One stored family-home item. The same record is the authoring request
/// shape, the stored content shape and the Admin read shape, so an Admin
/// client can round-trip what it reads without a second representation.
/// </summary>
public sealed record FamilyHomeItem(
    string? ArabicTitle,
    string? EnglishTitle,
    string? ArabicBody,
    string? EnglishBody,
    string? ImageKey,
    string? IconKey,
    FamilyHomeTarget? Target,
    Guid? ServiceId);

/// <summary>
/// Typed item payload of a create/update request. No JSON string is accepted
/// or returned anywhere in the family-home contract.
/// </summary>
public sealed record FamilyHomeItemRequest(
    string? ArabicTitle = null,
    string? EnglishTitle = null,
    string? ArabicBody = null,
    string? EnglishBody = null,
    string? ImageKey = null,
    string? IconKey = null,
    FamilyHomeTargetRequest? Target = null,
    Guid? ServiceId = null);

public sealed record FamilyHomeTargetRequest(string Type, Guid? ServiceId = null);

/// <summary>
/// Serializes and parses the stored item payload. Serialization happens in
/// exactly one place with one option set; unreadable stored content throws
/// instead of silently becoming an empty section.
/// </summary>
internal static class FamilyHomeItems
{
    // Web defaults keep camelCase property names stable across the write and
    // read paths and drop absent optional members from storage. The relaxed
    // encoder is deliberate: the Admin list search compares the stored JSON
    // text with the search term, and the default encoder would store Arabic
    // titles as "\u0645..." escapes that no Arabic search term can ever match.
    // The value is only ever deserialized back into typed records, never
    // emitted as markup, so relaxing the encoder does not change what a client
    // sees.
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

    public static IReadOnlyList<FamilyHomeItem> Normalize(
        IReadOnlyList<FamilyHomeItemRequest>? requests)
    {
        var items = new List<FamilyHomeItem>();
        foreach (FamilyHomeItemRequest request in requests ?? [])
        {
            items.Add(new FamilyHomeItem(
                NormalizeText(request.ArabicTitle),
                NormalizeText(request.EnglishTitle),
                NormalizeText(request.ArabicBody),
                NormalizeText(request.EnglishBody),
                NormalizeText(request.ImageKey),
                NormalizeText(request.IconKey),
                request.Target is null
                    ? null
                    : new FamilyHomeTarget(NormalizeText(request.Target.Type) ?? string.Empty, request.Target.ServiceId),
                request.ServiceId));
        }

        return items;
    }

    public static string Serialize(IReadOnlyList<FamilyHomeItem> items) =>
        JsonSerializer.Serialize(items, Options);

    public static IReadOnlyList<FamilyHomeItem> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<FamilyHomeItem>>(json, Options) ?? [];
        }
        catch (JsonException ex)
        {
            throw new DomainException($"Stored family home content is not readable: {ex.Message}");
        }
    }

    private static string? NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}

public sealed record FamilyHomeSectionPublicItem(
    Guid Id,
    string Kind,
    int DisplayOrder,
    IReadOnlyList<FamilyHomePublicItem> Items);

/// <summary>
/// One published Family read item. Featured entries carry live catalog
/// name/icon/type and never an editorial override or a price.
/// </summary>
public sealed record FamilyHomePublicItem(
    string? ArabicTitle,
    string? EnglishTitle,
    string? ArabicBody,
    string? EnglishBody,
    string? ImageKey,
    string? IconKey,
    string? TargetType,
    Guid? TargetServiceId,
    Guid? ServiceId,
    string? ServiceNameArabic,
    string? ServiceNameEnglish,
    string? ServiceIconKey,
    int? ServiceCaregiverType);

public sealed record FamilyHomeAdminSectionResponse(
    Guid Id,
    string Kind,
    int Version,
    string Status,
    int DisplayOrder,
    bool IsVisible,
    Guid? SourceId,
    IReadOnlyList<FamilyHomeItem> Items,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc,
    DateTime? PublishedOnUtc);

public sealed record FamilyHomeAdminPageResponse(
    IReadOnlyList<FamilyHomeAdminSectionResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record FamilyHomeReorderItem(Guid Id, int DisplayOrder);

public sealed record CreateFamilyHomeSectionRequest(
    string Kind,
    IReadOnlyList<FamilyHomeItemRequest>? Items = null,
    int DisplayOrder = 0,
    bool IsVisible = true);

public sealed record UpdateFamilyHomeSectionRequest(
    IReadOnlyList<FamilyHomeItemRequest>? Items = null,
    int DisplayOrder = 0,
    bool IsVisible = true,
    int ExpectedVersion = 0);

public sealed record SetFamilyHomeSectionVisibilityRequest(bool Value, int ExpectedVersion = 0);

public sealed record PublishFamilyHomeSectionRequest(int ExpectedVersion = 0);

public sealed record FamilyHomeReorderEntryRequest(Guid Id, int DisplayOrder);

public sealed record ReorderFamilyHomeSectionsRequest(List<FamilyHomeReorderEntryRequest>? Items);
