using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sanad.Modules.Families.Infrastructure.Payments;

public static class PaymobHmacCalculator
{
    public static string Calculate(JsonElement obj, string hmacSecret)
    {
        var builder = new StringBuilder();

        Append(builder, obj, "amount_cents");
        Append(builder, obj, "created_at");
        Append(builder, obj, "currency");
        Append(builder, obj, "error_occured");
        Append(builder, obj, "has_parent_transaction");
        Append(builder, obj, "id");
        Append(builder, obj, "integration_id");
        Append(builder, obj, "is_3d_secure");
        Append(builder, obj, "is_auth");
        Append(builder, obj, "is_capture");
        Append(builder, obj, "is_refunded");
        Append(builder, obj, "is_standalone_payment");
        Append(builder, obj, "is_voided");
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

    private static void Append(StringBuilder builder, JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        builder.Append(value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText());
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
}