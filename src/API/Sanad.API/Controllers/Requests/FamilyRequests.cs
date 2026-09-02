using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.Modules.Families.Domain.Families;

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
    FamilyRelationshipType RelationshipType,
    DateOnly DateOfBirth,
    string? DetailedAddress,
    string? HealthNotes);

public sealed record UpdateDependentRequest(
    FamilyRelationshipType RelationshipType,
    string ArabicFullName,
    string EnglishFullName,
    Gender Gender,
    DateOnly DateOfBirth,
    string? DetailedAddress,
    string? HealthNotes);

public sealed record CreateFamilyInvitationRequest(
    string Email,
    FamilyRole Role,
    FamilyRelationshipType RelationshipType);

public sealed record AcceptFamilyInvitationRequest(
    string Token);

public sealed record DeclineFamilyInvitationRequest(
    string Token);