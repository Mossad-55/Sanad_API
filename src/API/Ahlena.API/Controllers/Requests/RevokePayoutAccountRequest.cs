namespace Ahlena.API.Controllers.Requests;

public sealed record RevokePayoutAccountRequest(
    int ExpectedRevision,
    string Reason);
