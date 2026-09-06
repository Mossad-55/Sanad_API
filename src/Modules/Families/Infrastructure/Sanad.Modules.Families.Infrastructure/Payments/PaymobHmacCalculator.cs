using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sanad.Modules.Families.Infrastructure.Payments;

public static class PaymobHmacCalculator
{
    // Paymob transaction HMAC: concatenate these obj fields in order.
    // Missing or JSON null becomes "" (same as the dashboard formula).
    private static readonly string[] FieldOrder =
    [
        "amount_cents",
        "created_at",
        "currency",
        "error_occured",
        "has_parent_transaction",
        "id",
        "integration_id",
        "is_3d_secure",
        "is_auth",
        "is_capture",
        "is_refunded",
        "is_standalone_payment",
        "is_voided"
    ];

    public static string Calculate(JsonElement obj, string hmacSecret)
    {
        var builder = new StringBuilder();

        foreach (string field in FieldOrder)
        {
            Append(builder, obj, field);
        }

        AppendNested(builder, obj, "order", "id");
        Append(builder, obj, "owner");
        Append(builder, obj, "pending");
        AppendNested(builder, obj, "source_data", "pan");
        AppendNested(builder, obj, "source_data", "sub_type");
        AppendNested(builder, obj, "source_data", "type");
        Append(builder, obj, "success");

        byte[] key = Encoding.UTF8.GetBytes(hmacSecret);
        byte[] data = Encoding.UTF8.GetBytes(builder.ToString());

        return Convert.ToHexString(HMACSHA512.HashData(key, data)).ToLowerInvariant();
    }

    public static bool IsValid(JsonElement obj, string hmacSecret, string? providedHmac)
    {
        if (string.IsNullOrWhiteSpace(hmacSecret) || string.IsNullOrWhiteSpace(providedHmac))
        {
            return false;
        }

        string expected = Calculate(obj, hmacSecret);
        return FixedTimeHexEquals(expected, providedHmac);
    }

    public static bool FixedTimeHexEquals(string expectedHex, string providedHex)
    {
        string left = NormalizeHex(expectedHex);
        string right = NormalizeHex(providedHex);

        if (left.Length == 0 || right.Length == 0 || left.Length != right.Length)
        {
            return false;
        }

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(left),
                Convert.FromHexString(right));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
    }

    public static string? CoalesceProvidedHmac(
        string? queryHmac,
        JsonElement body,
        string? headerHmac)
    {
        if (!string.IsNullOrWhiteSpace(queryHmac))
        {
            return queryHmac;
        }

        if (body.ValueKind is JsonValueKind.Object
            && body.TryGetProperty("hmac", out JsonElement bodyHmac)
            && bodyHmac.ValueKind == JsonValueKind.String)
        {
            return bodyHmac.GetString();
        }

        return string.IsNullOrWhiteSpace(headerHmac) ? null : headerHmac;
    }

    private static string NormalizeHex(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..];
        }

        return trimmed.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        builder.Append(Format(value));
    }

    private static void AppendNested(
        StringBuilder builder,
        JsonElement element,
        string parentName,
        string propertyName)
    {
        if (element.TryGetProperty(parentName, out JsonElement parent)
            && parent.ValueKind is JsonValueKind.Object)
        {
            Append(builder, parent, propertyName);
        }
    }

    private static string Format(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => value.GetRawText()
        };
}
