namespace Sanad.API.Controllers.Requests;

public sealed record RenameBankRequest(
    string ArabicName,
    string EnglishName);
