namespace Sanad.API.Controllers.Requests;

public sealed record UpdatePayoutAccountRequest(
    string AccountHolderName,
    string BankCode,
    string Iban);
