namespace Sanad.API.Controllers.Requests;

public sealed record CreateBankRequest(
    string Code,
    string ArabicName,
    string EnglishName);
