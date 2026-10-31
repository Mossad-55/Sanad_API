using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Ahlena.API.OpenApi;

/// <summary>
/// PORTAL-03 metadata-only enrichment for the generated document. Derives
/// numeric enum values/names from their CLR definitions (never guessed) and
/// requires the non-null <c>value</c> property on verified single-Guid ID
/// wrapper structs. Runtime JSON shapes, converters, and wire formats are
/// unchanged. Only integer-typed enum schemas are enriched; string enums stay
/// untouched. Flags enums, if ever encountered, receive names/description but
/// no exhaustive <c>enum</c> constraint so valid combinations keep validating.
/// </summary>
public sealed class PortalApiSchemaTransformer : IOpenApiSchemaTransformer
{
    private const string IdWrapperNamespace =
        "Ahlena.BuildingBlocks.Domain.Primitives.Ids";

    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        Type? type = context.JsonTypeInfo?.Type;
        if (type is null)
            return Task.CompletedTask;

        Type effective = Nullable.GetUnderlyingType(type) ?? type;

        if (effective.IsEnum)
            EnrichEnum(schema, effective);
        else if (IsIdWrapper(effective))
            RequireValue(schema);

        return Task.CompletedTask;
    }

    private static void EnrichEnum(OpenApiSchema schema, Type type)
    {
        if (schema.Type is not null &&
            !schema.Type.Value.HasFlag(JsonSchemaType.Integer))
            return;

        Array values = Enum.GetValues(type);
        string[] names = values.Cast<object>().Select(value => Enum.GetName(type, value)!).ToArray();
        string pairs = string.Join(
            ", ",
            values.Cast<object>().Select((value, index) => $"{Convert.ToInt64(value)} = {names[index]}"));

        if (type.GetCustomAttribute<FlagsAttribute>() is null)
        {
            schema.Enum = values
                .Cast<object>()
                .Select(value => (JsonNode)JsonValue.Create(Convert.ToInt64(value)))
                .ToList();
        }

        schema.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        schema.Extensions["x-enumNames"] = new JsonNodeExtension(
            new JsonArray(names.Select(name => (JsonNode)JsonValue.Create(name)).ToArray()));
        schema.Description = string.IsNullOrWhiteSpace(schema.Description)
            ? $"Known values: {pairs}."
            : $"{schema.Description} Known values: {pairs}.";
    }

    private static bool IsIdWrapper(Type type) =>
        type.IsValueType &&
        !type.IsEnum &&
        string.Equals(type.Namespace, IdWrapperNamespace, StringComparison.Ordinal) &&
        type.Name.EndsWith("Id", StringComparison.Ordinal) &&
        type.GetProperty("Value")?.PropertyType == typeof(Guid);

    private static void RequireValue(OpenApiSchema schema)
    {
        if (schema.Properties is null || !schema.Properties.ContainsKey("value"))
            return;

        schema.Required ??= new HashSet<string>();
        schema.Required.Add("value");
    }
}
