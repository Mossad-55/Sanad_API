using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ahlena.API.Authorization;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.MedicalAccess;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Infrastructure.Persistence;

namespace Ahlena.UnitTests.API;

public sealed class AdminCaregiverMedicalAccessAuditTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public AdminCaregiverMedicalAccessAuditTests(PortalApiMetadataHost host) => _host = host;

    private static FamiliesDbContext CreateDb()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new FamiliesDbContext(options);
    }

    [Fact]
    public async Task List_ReturnsRowsAndProjectsFields()
    {
        await using var db = CreateDb();
        var audit = CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), UserId.New(), MedicalAccessGrantId.New(),
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
            MedicalAccessGrantType.Limited, true, false, false, DateTime.UtcNow.AddDays(1), DateTime.UtcNow, Guid.NewGuid().ToString());
        db.CaregiverMedicalAccessAudits.Add(audit);
        await db.SaveChangesAsync();

        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var result = await controller.List(cancellationToken: default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<AdminCaregiverMedicalAccessAuditController.CaregiverMedicalAccessAuditPage>(ok.Value);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal(audit.Id, page.Items[0].AuditId);
        Assert.Null(page.Items[0].ReasonCode);
    }

    [Fact]
    public async Task List_FiltersByDecisionAndAction()
    {
        await using var db = CreateDb();
        db.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Denied, "Families.AccessDenied",
            null, null, null, null, null, DateTime.UtcNow, Guid.NewGuid().ToString()));
        db.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
            CaregiverMedicalAccessAuditAction.RevokeGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
            null, null, null, null, null, DateTime.UtcNow, Guid.NewGuid().ToString()));
        await db.SaveChangesAsync();

        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var result = await controller.List(action: "CreateGrant", decision: "Denied", cancellationToken: default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<AdminCaregiverMedicalAccessAuditController.CaregiverMedicalAccessAuditPage>(ok.Value);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Denied", page.Items[0].Decision);
    }

    [Fact]
    public async Task List_FiltersByActorDependentGrantee()
    {
        await using var db = CreateDb();
        var actor = UserId.New();
        var dep = ElderlyId.New();
        var grantee = UserId.New();
        db.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(actor, dep, grantee, null,
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
            null, null, null, null, null, DateTime.UtcNow, Guid.NewGuid().ToString()));
        await db.SaveChangesAsync();

        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var result = await controller.List(actorUserId: actor.Value, dependentId: dep.Value, granteeUserId: grantee.Value, cancellationToken: default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<AdminCaregiverMedicalAccessAuditController.CaregiverMedicalAccessAuditPage>(ok.Value);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task List_FiltersStronglyTypedIdsInRelationalProvider()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FamiliesDbContext>().UseSqlite(connection).Options;
        await using var db = new FamiliesDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var actor = UserId.New();
        var dependent = ElderlyId.New();
        var grantee = UserId.New();
        db.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(
            actor, dependent, grantee, MedicalAccessGrantId.New(),
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed,
            null, MedicalAccessGrantType.Limited, true, false, false, DateTime.UtcNow.AddDays(2),
            DateTime.UtcNow, Guid.NewGuid().ToString()));
        db.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(
            UserId.New(), ElderlyId.New(), UserId.New(), MedicalAccessGrantId.New(),
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed,
            null, MedicalAccessGrantType.Limited, true, false, false, DateTime.UtcNow.AddDays(2),
            DateTime.UtcNow, Guid.NewGuid().ToString()));
        await db.SaveChangesAsync();

        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var result = await controller.List(
            actorUserId: actor.Value,
            dependentId: dependent.Value,
            granteeUserId: grantee.Value,
            cancellationToken: default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<AdminCaregiverMedicalAccessAuditController.CaregiverMedicalAccessAuditPage>(ok.Value);
        Assert.Single(page.Items);
        Assert.Equal(actor.Value, page.Items[0].ActorUserId);
        Assert.Equal(dependent.Value, page.Items[0].DependentId);
        Assert.Equal(grantee.Value, page.Items[0].GranteeUserId);
    }

    [Fact]
    public async Task List_StableOrderingAndPaging()
    {
        await using var db = CreateDb();
        var t = DateTime.UtcNow.AddMinutes(-5);
        var audits = new List<CaregiverMedicalAccessAudit>();
        for (int i = 0; i < 3; i++)
        {
            var audit = CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
                CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
                null, null, null, null, null, t, Guid.NewGuid().ToString());
            audits.Add(audit);
            db.CaregiverMedicalAccessAudits.Add(audit);
        }
        await db.SaveChangesAsync();

        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var result = await controller.List(page: 1, pageSize: 2, cancellationToken: default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<AdminCaregiverMedicalAccessAuditController.CaregiverMedicalAccessAuditPage>(ok.Value);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Guid[] expectedFirstPage = audits.OrderByDescending(x => x.Id).Take(2).Select(x => x.Id).ToArray();
        Assert.Equal(expectedFirstPage, page.Items.Select(x => x.AuditId).ToArray());

        var secondResult = await controller.List(page: 2, pageSize: 2, cancellationToken: default);
        var secondOk = Assert.IsType<OkObjectResult>(secondResult);
        var secondPage = Assert.IsType<AdminCaregiverMedicalAccessAuditController.CaregiverMedicalAccessAuditPage>(secondOk.Value);
        Assert.Equal(audits.OrderByDescending(x => x.Id).Last().Id, Assert.Single(secondPage.Items).AuditId);
    }

    [Fact]
    public async Task List_RejectsInvalidAction()
    {
        await using var db = CreateDb();
        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var result = await controller.List(action: "Nope", cancellationToken: default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task List_RejectsRangeOver12Months()
    {
        await using var db = CreateDb();
        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var from = DateTime.UtcNow.AddMonths(-13);
        var through = DateTime.UtcNow;
        var result = await controller.List(fromUtc: from, throughUtc: through, cancellationToken: default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task List_RejectsNonUtcBounds()
    {
        await using var db = CreateDb();
        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var local = DateTime.Now;
        var result = await controller.List(fromUtc: local, throughUtc: local.AddHours(1), cancellationToken: default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task List_RejectsOffsetOverflow()
    {
        await using var db = CreateDb();
        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var result = await controller.List(page: 2147483647, pageSize: 100, cancellationToken: default);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task List_ExcludesExpiredRows()
    {
        await using var db = CreateDb();
        db.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
            null, null, null, null, null, DateTime.UtcNow.AddMonths(-13), Guid.NewGuid().ToString()));
        db.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
            null, null, null, null, null, DateTime.UtcNow, Guid.NewGuid().ToString()));
        await db.SaveChangesAsync();

        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var result = await controller.List(cancellationToken: default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<AdminCaregiverMedicalAccessAuditController.CaregiverMedicalAccessAuditPage>(ok.Value);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task List_ProjectionHasNoSensitiveFields()
    {
        await using var db = CreateDb();
        db.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(UserId.New(), ElderlyId.New(), null, null,
            CaregiverMedicalAccessAuditAction.CreateGrant, CaregiverMedicalAccessAuditDecision.Allowed, null,
            null, null, null, null, null, DateTime.UtcNow, Guid.NewGuid().ToString()));
        await db.SaveChangesAsync();

        var controller = new AdminCaregiverMedicalAccessAuditController(db);
        var result = await controller.List(cancellationToken: default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<AdminCaregiverMedicalAccessAuditController.CaregiverMedicalAccessAuditPage>(ok.Value);
        var json = System.Text.Json.JsonSerializer.Serialize(page.Items[0]);
        Assert.DoesNotContain("notes", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("medication", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("SuperAdmin", "Normal", true)]
    [InlineData("SupportAdmin", "Normal", true)]
    [InlineData("ContentAdmin", "Normal", false)]
    [InlineData("Family", "Normal", false)]
    [InlineData("FinanceAdmin", "Normal", false)]
    [InlineData("SuperAdmin", "Restricted", false)]
    public async Task Policy_AuthorizationOutcomes(string accountType, string accessType, bool expected)
    {
        using var scope = _host.Services.CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var policyProvider = scope.ServiceProvider.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(AuthorizationPolicies.MedicalAccessAuditAdmin);
        Assert.NotNull(policy);

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new(AuthClaimNames.AccessType, accessType),
            new(AuthClaimNames.AccountType, accountType),
        };
        var principal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "Test"));
        var result = await authorizationService.AuthorizeAsync(principal, policy);
        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public void Controller_UsesDedicatedMedicalAccessAuditPolicy()
    {
        var authorize = Assert.Single(
            typeof(AdminCaregiverMedicalAccessAuditController)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.MedicalAccessAuditAdmin, authorize.Policy);
    }

    [Fact]
    public async Task OpenApi_ContainsAuditEndpointAndDto()
    {
        using var client = _host.CreatePortalClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var paths = root.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/admin/caregivers/medical-access-audit", out var pathItem));
        Assert.True(pathItem.TryGetProperty("get", out var getOp));
        Assert.True(getOp.GetProperty("responses").TryGetProperty("200", out var responseElement));
        var pageReference = responseElement.GetProperty("content").GetProperty("application/json").GetProperty("schema");
        var pageSchema = ResolveSchema(root, pageReference);
        Assert.Equal("object", pageSchema.GetProperty("type").GetString());
        var properties = pageSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("items", out var itemsProperty));
        Assert.Equal("array", itemsProperty.GetProperty("type").GetString());
        var itemSchema = ResolveSchema(root, itemsProperty.GetProperty("items"));
        var itemProperties = itemSchema.GetProperty("properties");
        foreach (var expected in new[]
        {
            "auditId", "occurredOnUtc", "actorUserId", "dependentId", "granteeUserId",
            "grantId", "action", "decision", "reasonCode", "grantType",
            "canViewRecords", "canEditRecords", "canShareWithOthers", "expiresOnUtc", "correlationId"
        })
        {
            Assert.True(itemProperties.TryGetProperty(expected, out _), $"Missing audit response field {expected}.");
        }
        foreach (var forbidden in new[] { "notes", "medicalProfile", "medicalHistory", "medications", "email", "phone", "arabicFullName", "englishFullName", "requestBody" })
        {
            Assert.False(itemProperties.TryGetProperty(forbidden, out _), $"Sensitive field {forbidden} must not appear in the audit response.");
        }
        var parameters = getOp.GetProperty("parameters");
        var paramNames = new List<string>();
        foreach (var p in parameters.EnumerateArray())
        {
            paramNames.Add(p.GetProperty("name").GetString()!);
        }
        foreach (var expected in new[] { "fromUtc", "throughUtc", "actorUserId", "dependentId", "granteeUserId", "action", "decision", "page", "pageSize" })
        {
            Assert.Contains(expected, paramNames);
        }
    }

    [Fact]
    public async Task FamilyGrantController_AuditWriteFailureReturns503()
    {
        UserId actor = UserId.New();
        var error = new Error("Families.AuditWriteFailed", "The medical access audit could not be recorded.");
        var controller = new DependentMedicalAccessGrantsController(new AuditFailureSender(error))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, actor.Value.ToString())],
                        "Test"))
                }
            }
        };

        var create = await controller.CreateMedicalAccessGrant(
            Guid.NewGuid(),
            new CreateMedicalAccessGrantRequest(
                "Limited", true, false, false, Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null),
            CancellationToken.None);
        var createResponse = Assert.IsType<ObjectResult>(create);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, createResponse.StatusCode);
        Assert.Equal(error, createResponse.Value);

        var revoke = await controller.DeleteMedicalAccessGrant(
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        var revokeResponse = Assert.IsType<ObjectResult>(revoke);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, revokeResponse.StatusCode);
        Assert.Equal(error, revokeResponse.Value);
    }

    private sealed class AuditFailureSender(Error error) : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                CreateMedicalAccessGrantCommand =>
                    Result<MedicalAccessGrant>.Failure(error),
                RevokeMedicalAccessGrantCommand =>
                    Result<bool>.Failure(error),
                _ => throw new NotSupportedException()
            };
            return Task.FromResult((TResponse)result);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static JsonElement ResolveSchema(JsonElement root, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out var reference)) return schema;
        var name = reference.GetString()!.Split('/').Last();
        return root.GetProperty("components").GetProperty("schemas").GetProperty(name);
    }
}
