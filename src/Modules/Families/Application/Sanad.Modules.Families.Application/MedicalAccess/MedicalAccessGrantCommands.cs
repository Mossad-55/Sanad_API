using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.MedicalAccess;

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
