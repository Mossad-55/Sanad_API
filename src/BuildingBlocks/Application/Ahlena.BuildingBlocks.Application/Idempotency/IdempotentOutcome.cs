namespace Ahlena.BuildingBlocks.Application.Idempotency;

/// <summary>
/// The result of an idempotent create. <see cref="Value"/> is the freshly
/// created response; a non-null <see cref="ReplayBody"/> means this is a retry
/// replay and the controller must return the stored status and the stored
/// byte-exact body instead of serializing <see cref="Value"/> again.
/// </summary>
public sealed record IdempotentOutcome<T>(
    T Value,
    int StatusCode,
    string? ReplayBody)
{
    public bool IsReplay => ReplayBody is not null;

    public static IdempotentOutcome<T> Fresh(
        T value,
        int statusCode) =>
        new(value, statusCode, null);

    public static IdempotentOutcome<T> Replay(
        int statusCode,
        string body) =>
        new(default!, statusCode, body);
}
