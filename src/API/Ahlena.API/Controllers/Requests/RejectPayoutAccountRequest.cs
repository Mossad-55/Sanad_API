namespace Ahlena.API.Controllers.Requests;

public sealed record RejectPayoutAccountRequest(
    int ExpectedRevision,
    string Reason);
