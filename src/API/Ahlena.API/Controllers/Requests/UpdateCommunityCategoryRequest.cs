namespace Ahlena.API.Controllers.Requests;

public sealed record UpdateCommunityCategoryRequest(string ArabicName, string EnglishName, bool IsActive);
