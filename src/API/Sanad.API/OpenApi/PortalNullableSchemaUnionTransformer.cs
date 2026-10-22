using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Sanad.API.OpenApi;

/// <summary>
/// Normalizes nullable scalar schemas into an explicit null-branch union:
/// any inline schema whose <see cref="OpenApiSchema.Type"/> carries the
/// <see cref="JsonSchemaType.Null"/> flag (a JSON-Schema <c>type: [x, "null"]</c>
/// array, the 3.1-style shape the generator emits for <c>Nullable&lt;T&gt;</c>
/// and NRT-nullable positional-record properties) is rewritten to
/// <c>anyOf: [valueSchema, { type: "null" }]</c>. A strict OpenAPI consumer
/// that only resolves nullability through <c>nullable: true</c> (3.0) or an
/// <c>anyOf</c>/<c>oneOf</c> union with a <c>{ type: "null" }</c> member reads
/// the union form; readers that accept the type-array form keep working.
/// <para>
/// Deliberately narrow: only plain scalar leaves (string/number/integer/boolean,
/// with or without a format) are rewritten. Object and array schemas, enum
/// schemas (already enriched by <see cref="PortalApiSchemaTransformer"/>), and
/// any schema that already carries a composition keep their generated form.
/// Non-nullable schemas carry no Null flag and pass through untouched, so
/// existing response shapes do not change. Reference proxies
/// (<see cref="OpenApiSchemaReference"/>) are skipped — shared component
/// schemas are visited through their own registration and must not be mutated
/// through a proxy. The transformation is idempotent: the wrapper carries no
/// type, so a second pass finds nothing to do.
/// </para>
/// </summary>
public sealed class PortalNullableSchemaUnionTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        Visit(schema);
        return Task.CompletedTask;
    }

    private static void Visit(IOpenApiSchema? node)
    {
        if (node is null)
            return;

        if (node is not OpenApiSchema concrete)
        {
            // References stand for shared components; the framework visits the
            // target through its own registration. Recursing here could mutate
            // a component's shared nullability from the wrong context or walk
            // a reference cycle.
            return;
        }

        // Convert (idempotent), then walk the children of the node as it now
        // stands: named properties, item/child compositions and
        // additional-properties. The framework's own traversal already covers
        // properties/items/polymorphic branches; this pass also covers
        // compositions and non-reference children it does not visit, including
        // branches this pass converted. Every branch is null-flag/one-visit
        // guarded, so repetition is harmless.
        _ = TryConvertToNullUnion(concrete);
        Walk(concrete.Properties?.Values);
        Walk(concrete.PatternProperties?.Values);
        Walk(concrete.AnyOf);
        Walk(concrete.AllOf);
        Walk(concrete.OneOf);
        Visit(concrete.Items);
        Visit(concrete.Not);
        Visit(concrete.AdditionalProperties);
    }

    private static void Walk(IEnumerable<IOpenApiSchema>? branches)
    {
        if (branches is null)
            return;

        foreach (IOpenApiSchema branch in branches)
            Visit(branch);
    }

    private static bool TryConvertToNullUnion(OpenApiSchema schema)
    {
        if (schema.Type is not { } type || !type.HasFlag(JsonSchemaType.Null))
        {
            // Non-nullable schemas (the overwhelming majority) and the pure
            // null literal have nothing to normalize.
            return false;
        }

        JsonSchemaType nonNull = type & ~JsonSchemaType.Null;
        if (nonNull == default || (nonNull & (JsonSchemaType.Object | JsonSchemaType.Array)) != 0)
        {
            // Structural kinds (objects, arrays) keep their generated form.
            // This pass deliberately scopes itself to the scalar leaves the
            // strict consumer provably rejects; a strict 3.0-style reader may
            // still treat a structural type:["object","null"] as invalid, and
            // widening the rewrite to structural kinds is future scope.
            return false;
        }

        // Skip composed, enumerated or mapped schemas: nullable enums already
        // arrive as unions, dictionary/object shapes and wrappers are covered
        // by the structural guard above.
        if (schema.AnyOf is { Count: > 0 } ||
            schema.OneOf is { Count: > 0 } ||
            schema.AllOf is { Count: > 0 } ||
            schema.Enum is { Count: > 0 })
        {
            return false;
        }

        // The value branch keeps every keyword the leaf had (type minus Null,
        // format, numeric/string constraints, example, default).
        OpenApiSchema valueBranch = (OpenApiSchema)schema.CreateShallowCopy();
        valueBranch.Type = nonNull;

        schema.AnyOf =
        [
            valueBranch,
            new OpenApiSchema { Type = JsonSchemaType.Null }
        ];

        // Move the scalar keywords onto the value branch only; the wrapper
        // describes the union. Clearing Type also makes the transform
        // idempotent (a re-run finds no Null flag) and keeps the 3.1 writer
        // from re-emitting the type array next to the anyOf.
        schema.Type = null;
        schema.Format = null;
        schema.Const = null;
        schema.Pattern = null;
        schema.MaxLength = null;
        schema.MinLength = null;
        schema.Minimum = null;
        schema.Maximum = null;
        schema.ExclusiveMaximum = null;
        schema.ExclusiveMinimum = null;
        schema.MultipleOf = null;
        schema.Default = null;
        schema.Example = null;
        schema.Examples = null;

        return true;
    }
}
