namespace Sanad.API.Controllers.Requests;

public sealed record CreateCityRequest(
    Guid GovernorateId,
    string ArabicName,
    string EnglishName);