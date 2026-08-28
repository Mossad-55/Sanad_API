namespace Sanad.API.Controllers.Requests;

public sealed record RenameAreaRequest(
    string ArabicName,
    string EnglishName);