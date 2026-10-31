namespace Ahlena.API.Controllers.Requests;

public sealed record RenameProfessionalTitleRequest(
    string ArabicName,
    string EnglishName);