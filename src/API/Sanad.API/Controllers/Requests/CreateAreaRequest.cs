namespace Sanad.API.Controllers.Requests;

public sealed record CreateAreaRequest(
    Guid CityId,
    string ArabicName,
    string EnglishName);