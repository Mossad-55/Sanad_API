namespace Sanad.API.Controllers.Requests;

public sealed record CreateAcademicDegreeRequest(
    string ArabicName,
    string EnglishName,
    bool IsActive);