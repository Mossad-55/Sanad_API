namespace Ahlena.API.Controllers.Requests;

public sealed record RenameCityRequest(
    string ArabicName,
    string EnglishName);