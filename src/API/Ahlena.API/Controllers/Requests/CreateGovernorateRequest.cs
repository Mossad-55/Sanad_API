namespace Ahlena.API.Controllers.Requests;

public sealed record CreateGovernorateRequest(
    string ArabicName,
    string EnglishName);