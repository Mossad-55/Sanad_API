namespace Sanad.API.Controllers.Requests;

public sealed record ApprovePayoutAccountRequest(
    int ExpectedRevision,
    string VerificationSource,
    string? Reference);
