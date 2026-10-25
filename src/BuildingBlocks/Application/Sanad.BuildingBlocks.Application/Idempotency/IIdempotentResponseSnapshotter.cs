namespace Sanad.BuildingBlocks.Application.Idempotency;

/// <summary>
/// Serializes a create response exactly as MVC will write it to the wire, so
/// the stored snapshot can be replayed byte-for-byte without recomputing the
/// response from the (possibly changed) business row. The API host supplies
/// the implementation over MVC's own <c>JsonOptions</c>.
/// </summary>
public interface IIdempotentResponseSnapshotter
{
    string Snapshot<T>(T response);
}
