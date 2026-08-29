namespace Sanad.API.Controllers.Requests;

public sealed record CreateProfessionalTitleRequest(
    string ArabicName,
    string EnglishName,
    bool IsActive);