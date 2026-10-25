using System.Security.Cryptography;
using System.Text;

namespace Sanad.BuildingBlocks.Application.Idempotency;

/// <summary>
/// Canonical request fingerprint for an idempotent create.
/// <para>
/// The operation name and the ordered field pairs are serialized as
/// <c>key\0value\0…</c>, UTF-8 encoded, SHA-256 hashed and rendered as
/// lowercase hex. A null value serializes as the literal <c>&lt;null&gt;</c>.
/// Callers pass the canonical request body plus every route/path identifier
/// and trim/normalize values before calling; server-derived inputs (the clock,
/// the caller's current date, correlation ids, resolved intake snapshots) are
/// deliberately excluded so a genuine retry produces the same fingerprint.
/// </para>
/// </summary>
public static class IdempotencyFingerprint
{
    private const string NullValue = "<null>";

    public static string Compute(
        string operation,
        IEnumerable<KeyValuePair<string, string?>> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(fields);

        var builder = new StringBuilder();

        Append(builder, operation);

        foreach (KeyValuePair<string, string?> field in fields)
        {
            Append(builder, field.Key);
            Append(builder, field.Value);
        }

        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(builder.ToString()));

        return Convert.ToHexStringLower(hash);
    }

    public static string Compute(
        string operation,
        params (string Key, string? Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        return Compute(
            operation,
            fields.Select(field =>
                new KeyValuePair<string, string?>(field.Key, field.Value)));
    }

    private static void Append(
        StringBuilder builder,
        string? value)
    {
        builder.Append(value ?? NullValue);
        builder.Append('\0');
    }

    /// <summary>GUID <c>D</c> format, lowercase, for fingerprint fields.</summary>
    public static string Id(Guid value) => value.ToString("D");

    /// <summary>GUID <c>D</c> format, lowercase, for fingerprint fields.</summary>
    public static string? Id(Guid? value) =>
        value.HasValue ? Id(value.Value) : null;

    /// <summary>Invariant <c>yyyy-MM-dd</c> date text.</summary>
    public static string Date(DateOnly value) =>
        value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Invariant <c>HH:mm</c> time text.</summary>
    public static string Time(TimeOnly value) =>
        value.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Enum name, so a numeric wire value and its name agree.</summary>
    public static string Enum<TEnum>(TEnum value) where TEnum : struct, System.Enum =>
        value.ToString();

    /// <summary>Invariant decimal text.</summary>
    public static string Decimal(decimal value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Trimmed text; an absent value stays null.</summary>
    public static string? Text(string? value) => value?.Trim();
}
