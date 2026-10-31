namespace Ahlena.API.Controllers.Requests;

public sealed record RenameSpecializationRequest(
    string ArabicName,
    string EnglishName);