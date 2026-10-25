using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Idempotency;

namespace Sanad.API.Idempotency;

/// <summary>
/// Stores the replay body with the exact serializer MVC uses to write the
/// live response, so a retried create returns the same bytes it originally
/// returned. The snapshot is taken once, before persistence, never recomputed
/// from the mutated business row.
/// </summary>
public sealed class MvcIdempotentResponseSnapshotter(
    IOptions<JsonOptions> jsonOptions) : IIdempotentResponseSnapshotter
{
    private readonly JsonSerializerOptions _options =
        jsonOptions.Value.JsonSerializerOptions;

    public string Snapshot<T>(T response) =>
        JsonSerializer.Serialize(response, _options);
}
