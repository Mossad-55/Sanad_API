namespace Sanad.API.Controllers.Requests;

public sealed record RenameLanguageRequest(
    string ArabicName,
    string EnglishName);