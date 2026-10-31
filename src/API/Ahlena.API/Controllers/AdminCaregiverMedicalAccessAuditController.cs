using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Families.Domain.Elderlies;

namespace Ahlena.API.Controllers;

[Authorize(Policy = AuthorizationPolicies.MedicalAccessAuditAdmin)]
[Route("api/v1/admin/caregivers/medical-access-audit")]
public sealed class AdminCaregiverMedicalAccessAuditController(IFamiliesDbContext families) : ApiControllerBase
{
    public sealed record CaregiverMedicalAccessAuditResponse(
        Guid AuditId,
        DateTime OccurredOnUtc,
        Guid ActorUserId,
        Guid DependentId,
        Guid? GranteeUserId,
        Guid? GrantId,
        string Action,
        string Decision,
        string? ReasonCode,
        string? GrantType,
        bool? CanViewRecords,
        bool? CanEditRecords,
        bool? CanShareWithOthers,
        DateTime? ExpiresOnUtc,
        string CorrelationId);

    public sealed record CaregiverMedicalAccessAuditPage(
        IReadOnlyList<CaregiverMedicalAccessAuditResponse> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages);

    [HttpGet]
    [ProducesResponseType(typeof(CaregiverMedicalAccessAuditPage), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? throughUtc = null,
        [FromQuery] Guid? actorUserId = null,
        [FromQuery] Guid? dependentId = null,
        [FromQuery] Guid? granteeUserId = null,
        [FromQuery] string? action = null,
        [FromQuery] string? decision = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        // Validate time filters.
        bool hasFrom = fromUtc.HasValue;
        bool hasThrough = throughUtc.HasValue;
        if (hasFrom != hasThrough)
        {
            return ValidationFailed("Both fromUtc and throughUtc are required.");
        }

        var now = DateTime.UtcNow;
        var cutoff = now.AddMonths(-12);

        DateTime lower;
        DateTime upper;
        if (hasFrom && hasThrough)
        {
            if (fromUtc!.Value.Kind != DateTimeKind.Utc || throughUtc!.Value.Kind != DateTimeKind.Utc)
            {
                return ValidationFailed("Bounds must be UTC.");
            }
            lower = fromUtc.Value;
            upper = throughUtc.Value;
            if (lower >= upper)
            {
                return ValidationFailed("fromUtc must be before throughUtc.");
            }
            DateTime maxUpper;
            try
            {
                maxUpper = lower.AddMonths(12);
            }
            catch (ArgumentOutOfRangeException)
            {
                maxUpper = DateTime.MaxValue;
            }
            if (upper > maxUpper)
            {
                return ValidationFailed("Range must not exceed 12 months.");
            }
        }
        else
        {
            // default last 30 days
            upper = now;
            lower = now.AddDays(-30);
        }

        // clamp page/pageSize
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Parse action/decision enums
        CaregiverMedicalAccessAuditAction? actionEnum = null;
        if (!string.IsNullOrWhiteSpace(action))
        {
            if (!Enum.TryParse<CaregiverMedicalAccessAuditAction>(action, true, out var parsed) || !Enum.IsDefined(parsed))
                return ValidationFailed("Invalid action.");
            actionEnum = parsed;
        }
        CaregiverMedicalAccessAuditDecision? decisionEnum = null;
        if (!string.IsNullOrWhiteSpace(decision))
        {
            if (!Enum.TryParse<CaregiverMedicalAccessAuditDecision>(decision, true, out var parsed) || !Enum.IsDefined(parsed))
                return ValidationFailed("Invalid decision.");
            decisionEnum = parsed;
        }

        var query = families.CaregiverMedicalAccessAudits.AsNoTracking()
            .Where(x => x.OccurredOnUtc >= cutoff) // retention exclusion
            .Where(x => x.OccurredOnUtc >= lower && x.OccurredOnUtc < upper);

        if (actorUserId.HasValue)
        {
            UserId actor = new(actorUserId.Value);
            query = query.Where(x => x.ActorUserId == actor);
        }

        if (dependentId.HasValue)
        {
            ElderlyId dependent = new(dependentId.Value);
            query = query.Where(x => x.DependentId == dependent);
        }

        if (granteeUserId.HasValue)
        {
            UserId grantee = new(granteeUserId.Value);
            query = query.Where(x => x.GranteeUserId.HasValue && x.GranteeUserId.Value == grantee);
        }
        if (actionEnum.HasValue) query = query.Where(x => x.Action == actionEnum.Value);
        if (decisionEnum.HasValue) query = query.Where(x => x.Decision == decisionEnum.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        long skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue)
        {
            return ValidationFailed("Page offset is too large.");
        }

        var rows = await query
            .OrderByDescending(x => x.OccurredOnUtc)
            .ThenByDescending(x => x.Id)
            .Skip((int)skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new CaregiverMedicalAccessAuditResponse(
            x.Id,
            x.OccurredOnUtc,
            x.ActorUserId.Value,
            x.DependentId.Value,
            x.GranteeUserId?.Value,
            x.GrantId?.Value,
            x.Action.ToString(),
            x.Decision.ToString(),
            x.ReasonCode,
            x.GrantType?.ToString(),
            x.CanViewRecords,
            x.CanEditRecords,
            x.CanShareWithOthers,
            x.ExpiresOnUtc,
            x.CorrelationId)).ToList();

        return Ok(new CaregiverMedicalAccessAuditPage(items, page, pageSize, totalCount, totalPages));
    }

    private BadRequestObjectResult ValidationFailed(string detail) =>
        BadRequest(new ProblemDetails
        {
            Type = "https://httpstatuses.com/400",
            Title = "Validation Failed",
            Status = StatusCodes.Status400BadRequest,
            Detail = detail,
            Instance = HttpContext?.Request?.Path.Value,
            Extensions =
            {
                ["code"] = "Api.Validation.Failed",
                ["traceId"] = HttpContext?.TraceIdentifier
            }
        });
}
