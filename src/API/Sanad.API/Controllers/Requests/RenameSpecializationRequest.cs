namespace Sanad.API.Controllers.Requests;

public sealed record RenameSpecializationRequest(
    string ArabicName,
    string EnglishName);