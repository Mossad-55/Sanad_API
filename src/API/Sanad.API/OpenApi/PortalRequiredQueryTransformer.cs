using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Sanad.API.OpenApi;

/// <summary>
/// PORTAL-03 metadata-only correction: the five business-required parent/date
/// query parameters stay required in the generated document. Runtime binding
/// and omission behavior are unchanged (missing values still flow through the
/// existing handlers, which return their established outcomes).
/// </summary>
public sealed class PortalRequiredQueryTransformer : IOpenApiOperationTransformer
{
    private static readonly (string Path, string Name)[] RequiredQueries =
    [
        ("/api/v1/admin/care-homes/license-expirations", "through"),
        ("/api/v1/lookups/cities", "governorateId"),
        ("/api/v1/admin/lookups/cities", "governorateId"),
        ("/api/v1/lookups/areas", "cityId"),
        ("/api/v1/admin/lookups/areas", "cityId")
    ];

    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        string path = "/" + (context.Description.RelativePath ?? string.Empty).TrimStart('/');

        foreach ((string requiredPath, string name) in RequiredQueries)
        {
            if (!string.Equals(path, requiredPath, StringComparison.Ordinal))
                continue;

            if (operation.Parameters is null)
                continue;

            foreach (IOpenApiParameter parameter in operation.Parameters)
            {
                if (parameter is OpenApiParameter openApiParameter &&
                    string.Equals(openApiParameter.Name, name, StringComparison.Ordinal) &&
                    openApiParameter.In == ParameterLocation.Query)
                {
                    openApiParameter.Required = true;
                }
            }
        }

        return Task.CompletedTask;
    }
}
