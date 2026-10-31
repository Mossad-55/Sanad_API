namespace Ahlena.API.Controllers.Requests;

public sealed record RenameAreaRequest(
    string ArabicName,
    string EnglishName);