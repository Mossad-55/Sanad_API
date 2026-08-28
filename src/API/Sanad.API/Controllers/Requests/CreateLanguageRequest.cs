namespace Sanad.API.Controllers.Requests;

public sealed record CreateLanguageRequest(
    string Code,
    string ArabicName,
    string EnglishName);