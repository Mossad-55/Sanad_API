namespace Sanad.API.Controllers.Requests;

public sealed record RenameGovernorateRequest(
    string ArabicName,
    string EnglishName);