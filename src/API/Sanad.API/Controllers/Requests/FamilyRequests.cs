using Sanad.BuildingBlocks.Domain.Enums;

namespace Sanad.API.Controllers.Requests;

public sealed record BootstrapFamilyRequest(
    string? Name);

public sealed record RenameFamilyRequest(
    string Name);

// Multipart form for adding a dependent. The photo is the separate
// IFormFile action parameter, matching the certificate endpoints.
public sealed record AddDependentRequest(
    string ArabicFullName,
    string EnglishFullName,
    string PhoneNumber,
    Gender Gender,
    DateOnly DateOfBirth,
    string? DetailedAddress,
    string? HealthNotes);

public sealed record UpdateDependentRequest(
    string ArabicFullName,
    string EnglishFullName,
    Gender Gender,
    DateOnly DateOfBirth,
    string? DetailedAddress,
    string? HealthNotes);