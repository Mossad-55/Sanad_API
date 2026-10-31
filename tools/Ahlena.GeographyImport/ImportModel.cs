using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace Ahlena.GeographyImport;

public enum LocationLevel { Governorate = 1, City = 2, Area = 3 }
public enum ImportAction { Insert, Keep, KeepInactive, Conflict }
public sealed record SourceLocation(LocationLevel Level, string Key, string? ParentKey, string ArabicName, string EnglishName);
public sealed record ExistingLocation(LocationLevel Level, Guid Id, Guid? ParentId, string ArabicName,
    string EnglishName, bool IsActive, DateTime CreatedOnUtc, DateTime UpdatedOnUtc);
public sealed record PlannedLocation(LocationLevel Level, string SourceKey, Guid Id, Guid? ParentId,
    string ArabicName, string EnglishName, ImportAction Action, bool IsActive, string? ConflictCode)
{
    public IReadOnlyList<Guid> MatchingTargetIds { get; init; } = [];
}
public sealed record ImportPlan(int Version, string CatalogueSha256, string TargetFingerprint,
    string BaselineFingerprint, DateTime CreatedOnUtc, IReadOnlyList<PlannedLocation> Rows,
    IReadOnlyList<ExistingLocation> PreservedExtras)
{
    public bool HasConflicts => Rows.Any(row => row.Action == ImportAction.Conflict);
    public string? Reconciliation { get; init; }
    public int InsertCount => Rows.Count(row => row.Action == ImportAction.Insert);
}
public sealed class ImportRejectedException(string code) : Exception(code);

public static class ImportJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

public sealed record ImportCatalogue(string Sha256, IReadOnlyList<SourceLocation> Rows)
{
    public const string AcceptedV1Sha256 = "E048767A00F5DE28B32E8F4E1E2B2BCBC4FA52C19BEA0C0D14D085DDB5AB8A8E";

    public static ImportCatalogue Parse(byte[] bytes, string expectedSha256)
    {
        if (bytes.Length is 0 or > 2_000_000 || ImportJson.Hash(bytes) != expectedSha256)
            throw new ImportRejectedException("CatalogueHashOrSize");
        using var document = JsonDocument.Parse(bytes);
        var rows = new List<SourceLocation>();
        foreach (var (collection, level, parent) in new[]
        {
            ("governorates", LocationLevel.Governorate, (string?)null),
            ("cities", LocationLevel.City, "governorateKey"),
            ("areas", LocationLevel.Area, "cityKey")
        })
        {
            var array = document.RootElement.GetProperty(collection);
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() == 0)
                throw new ImportRejectedException("CatalogueCollection");
            foreach (var row in array.EnumerateArray())
                rows.Add(new SourceLocation(level, Text(row, "key"), parent is null ? null : Text(row, parent),
                    Text(row, "arabicName"), Text(row, "englishName")));
        }
        return Create(expectedSha256, rows);
    }

    public static ImportCatalogue Create(string hash, IReadOnlyList<SourceLocation> rows)
    {
        if (rows.Count is 0 or > 10_000 || string.IsNullOrWhiteSpace(hash)) throw new ImportRejectedException("CatalogueSize");
        var keys = new Dictionary<string, SourceLocation>(StringComparer.Ordinal);
        var names = new HashSet<(LocationLevel, string?, string, bool)>();
        foreach (var row in rows)
        {
            if (!Enum.IsDefined(row.Level) || string.IsNullOrWhiteSpace(row.Key) || row.Key != row.Key.Trim() || !keys.TryAdd(row.Key, row))
                throw new ImportRejectedException("CatalogueKey");
            if (!ValidName(row.ArabicName) || !ValidName(row.EnglishName) ||
                !Regex.IsMatch(row.ArabicName, "[\\u0600-\\u06FF]") || !Regex.IsMatch(row.EnglishName, "[A-Za-z]"))
                throw new ImportRejectedException("CatalogueName");
            if (!names.Add((row.Level, row.ParentKey, row.ArabicName, true)) ||
                !names.Add((row.Level, row.ParentKey, row.EnglishName, false))) throw new ImportRejectedException("CatalogueDuplicateName");
        }
        foreach (var row in rows)
        {
            if (row.Level == LocationLevel.Governorate ? row.ParentKey is not null :
                row.ParentKey is null || !keys.TryGetValue(row.ParentKey, out var parent) || (int)parent.Level != (int)row.Level - 1)
                throw new ImportRejectedException("CatalogueParent");
        }
        return new(hash, rows.OrderBy(row => row.Level).ThenBy(row => row.Key, StringComparer.Ordinal).ToArray());
    }

    private static string Text(JsonElement row, string property) => row.GetProperty(property).GetString()
        ?? throw new ImportRejectedException("CatalogueNull");
    private static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 150 && name == name.Trim() &&
        !name.Any(char.IsControl) && !name.Contains('\uFFFD');
}
