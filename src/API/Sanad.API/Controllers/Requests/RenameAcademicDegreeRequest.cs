namespace Sanad.API.Controllers.Requests;

public sealed record RenameAcademicDegreeRequest(
    string ArabicName,
    string EnglishName);