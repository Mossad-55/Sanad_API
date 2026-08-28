namespace Sanad.API.Controllers.Requests;

public sealed record RenameServiceRequest(
    string ArabicName,
    string EnglishName);