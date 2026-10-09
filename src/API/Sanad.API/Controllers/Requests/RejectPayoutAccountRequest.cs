namespace Sanad.API.Controllers.Requests;

public sealed record RejectPayoutAccountRequest(
    int ExpectedRevision,
    string Reason);
