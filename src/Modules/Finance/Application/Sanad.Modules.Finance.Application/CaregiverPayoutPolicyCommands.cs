using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Application;

public sealed record CaregiverPayoutPolicyRates(int PayoutDelayHours, int Version);
public sealed record CaregiverPayoutPolicyHistoryItem(Guid Id, int PayoutDelayHours, int Version, DateTime EffectiveOnUtc, DateTime CreatedOnUtc, bool IsActive);

public interface ICaregiverPayoutPolicyReader
{
    Task<CaregiverPayoutPolicyRates?> GetEffectiveAsync(DateTime utcNow, CancellationToken cancellationToken);
    Task<IReadOnlyList<CaregiverPayoutPolicyHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken);
}

public interface ICaregiverPayoutPolicyWriter
{
    Task<Result<Guid>> CreateAsync(int payoutDelayHours, int requestedVersion, DateTime effectiveOnUtc, CancellationToken cancellationToken);
}

public sealed record CreateCaregiverPayoutPolicyCommand(int PayoutDelayHours, int Version, DateTime EffectiveOnUtc) : ICommand<Guid>;
public sealed record CaregiverPayoutPolicyResponse(Guid Id, int PayoutDelayHours, int Version, DateTime EffectiveOnUtc, DateTime CreatedOnUtc, bool IsActive);
public sealed record GetCurrentCaregiverPayoutPolicyQuery : IQuery<CaregiverPayoutPolicyResponse?>;
public sealed record GetCaregiverPayoutPolicyHistoryQuery : IQuery<IReadOnlyList<CaregiverPayoutPolicyResponse>>;

public static class CaregiverPayoutPolicyErrors
{
    public static readonly Error Invalid = new("Finance.PayoutPolicy.Invalid", "The payout policy is invalid.");
    public static readonly Error VersionConflict = new("Finance.PayoutPolicy.VersionConflict", "The payout policy version must be greater than the current Finance version.");
    public static readonly Error Conflict = new("Finance.PayoutPolicy.Conflict", "The payout policy configuration changed concurrently.");
    public static readonly Error Missing = new("Finance.PayoutPolicy.Missing", "No effective caregiver payout policy exists.");

    public static CaregiverPayoutPolicyResponse Map(CaregiverPayoutPolicy x, bool isActive) =>
        new(x.Id, x.PayoutDelayHours, x.Version, x.EffectiveOnUtc, x.CreatedOnUtc, isActive);
}

public sealed class CreateCaregiverPayoutPolicyHandler(ICaregiverPayoutPolicyWriter writer) : ICommandHandler<CreateCaregiverPayoutPolicyCommand, Guid>
{
    public async Task<Result<Guid>> Handle(CreateCaregiverPayoutPolicyCommand request, CancellationToken ct)
    {
        try
        {
            return await writer.CreateAsync(request.PayoutDelayHours, request.Version, request.EffectiveOnUtc, ct);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException ex)
        {
            return Result<Guid>.Failure(new("Finance.PayoutPolicy.Invalid", ex.Message));
        }
    }
}

public sealed class GetCurrentCaregiverPayoutPolicyHandler(IFinanceDbContext db) : IQueryHandler<GetCurrentCaregiverPayoutPolicyQuery, CaregiverPayoutPolicyResponse?>
{
    public async Task<Result<CaregiverPayoutPolicyResponse?>> Handle(GetCurrentCaregiverPayoutPolicyQuery request, CancellationToken ct)
    {
        var current = await db.CaregiverPayoutPolicies.AsNoTracking()
            .Where(x => x.EffectiveOnUtc <= DateTime.UtcNow)
            .OrderByDescending(x => x.Version)
            .FirstOrDefaultAsync(ct);
        return Result<CaregiverPayoutPolicyResponse?>.Success(
            current is null ? null : CaregiverPayoutPolicyErrors.Map(current, true));
    }
}

public sealed class GetCaregiverPayoutPolicyHistoryHandler(ICaregiverPayoutPolicyReader reader) : IQueryHandler<GetCaregiverPayoutPolicyHistoryQuery, IReadOnlyList<CaregiverPayoutPolicyResponse>>
{
    public async Task<Result<IReadOnlyList<CaregiverPayoutPolicyResponse>>> Handle(GetCaregiverPayoutPolicyHistoryQuery request, CancellationToken ct)
        => (await reader.GetHistoryAsync(ct))
            .Select(x => new CaregiverPayoutPolicyResponse(x.Id, x.PayoutDelayHours, x.Version, x.EffectiveOnUtc, x.CreatedOnUtc, x.IsActive))
            .ToList();
}
