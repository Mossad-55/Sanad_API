using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Domain.Elderlies;

namespace Ahlena.Modules.Families.Application.MedicalAccess;

public sealed record CreateMedicalAccessGrantRequest(
    string GrantType,
    bool CanViewRecords,
    bool CanEditRecords,
    bool CanShareWithOthers,
    Guid GranteeUserId,
    DateTime? ExpiresOnUtc,
    string? Notes);

public sealed record CreateMedicalAccessGrantCommand(
    ElderlyId DependentId,
    UserId GrantedByUserId,
    UserId GranteeUserId,
    MedicalAccessGrantType GrantType,
    bool CanViewRecords,
    bool CanEditRecords,
    bool CanShareWithOthers,
    DateTime? ExpiresOnUtc,
    string? Notes) : ICommand<MedicalAccessGrant>;

public sealed record RevokeMedicalAccessGrantCommand(
    ElderlyId DependentId,
    MedicalAccessGrantId GrantId,
    UserId RevokedByUserId) : ICommand<bool>;
