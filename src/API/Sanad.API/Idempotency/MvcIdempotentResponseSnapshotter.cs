using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Idempotency;

namespace Sanad.API.Idempotency;

/// <summary>
/// Stores the replay body with the exact wire representation MVC writes for
/// the live response, so a retried create returns the same bytes it originally
/// returned. The snapshot is taken once, before persistence, never recomputed
/// from the mutated business row.
/// <para>
/// MVC's output formatter writes non-ASCII text as raw UTF-8, while a
/// <see cref="JsonSerializer"/> call on the same options escapes it as
/// <c>\uXXXX</c>. The snapshot therefore clones the MVC options and applies the
/// same relaxed JavaScript encoder, so identical values serialize to identical
/// bytes on the fresh and replayed responses. Property naming, converters,
/// number handling and every other MVC setting are inherited unchanged.
/// </para>
/// </summary>
public sealed class MvcIdempotentResponseSnapshotter : IIdempotentResponseSnapshotter
{
    private readonly JsonSerializerOptions _options;

    public MvcIdempotentResponseSnapshotter(IOptions<JsonOptions> jsonOptions)
    {
        _options = new JsonSerializerOptions(jsonOptions.Value.JsonSerializerOptions)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    public string Snapshot<T>(T response) =>
        JsonSerializer.Serialize(response, _options);
}
