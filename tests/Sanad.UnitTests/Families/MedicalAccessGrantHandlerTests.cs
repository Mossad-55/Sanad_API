using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.MedicalAccess;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.UnitTests.Support;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Sanad.UnitTests.Families;

public sealed class MedicalAccessGrantHandlerTests
{
    [Fact]
    public async Task Create_RejectsDuplicateActiveGrantForSameDependentAndGrantee()
    {
        await using var db = CreateDb();
        var fixture = AddFixture(db);
        db.MedicalAccessGrants.Add(MedicalAccessGrant.Create(
            fixture.Elderly.Id, fixture.Owner, fixture.Grantee, MedicalAccessGrantType.Limited,
            true, false, false, DateTime.UtcNow));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Command(fixture), default);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.GrantExists", result.Error.Code);
        Assert.Empty(db.CaregiverMedicalAccessAudits);
    }

    [Fact]
    public async Task Create_AllowsReissueWhenPreviousGrantHasExpired()
    {
        await using var db = CreateDb();
        var fixture = AddFixture(db);
        var expiredAt = DateTime.UtcNow.AddMinutes(-1);
        db.MedicalAccessGrants.Add(MedicalAccessGrant.Create(
            fixture.Elderly.Id, fixture.Owner, fixture.Grantee, MedicalAccessGrantType.Limited,
            true, false, false, expiredAt.AddMinutes(-5), expiredAt));
        await db.SaveChangesAsync();

        var result = await Handler(db).Handle(Command(fixture), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await db.MedicalAccessGrants.CountAsync());
    }

    [Fact]
    public async Task Create_RejectsNonMedicalCaregiverRecipient()
    {
        await using var db = CreateDb();
        var fixture = AddFixture(db);
        var result = await new CreateMedicalAccessGrantCommandHandler(db, new FakeGateway(false))
            .Handle(Command(fixture), default);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.InvalidGrant", result.Error.Code);
        Assert.Empty(db.MedicalAccessGrants);
        Assert.Empty(db.CaregiverMedicalAccessAudits);
    }

    private static CreateMedicalAccessGrantCommand Command(Fixture fixture) => new(
        fixture.Elderly.Id, fixture.Owner, fixture.Grantee, MedicalAccessGrantType.Limited,
        true, false, false, DateTime.UtcNow.AddDays(30), "test");

    private static CreateMedicalAccessGrantCommandHandler Handler(FamiliesDbContext db) =>
        new(db, new FakeGateway(true));

    private static Fixture AddFixture(FamiliesDbContext db)
    {
        var owner = UserId.New();
        var grantee = UserId.New();
        var family = Family.Create(owner, "Grant tests");
        var elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father,
            FullName.Create("Test"), FullName.Create("Test"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();
        return new Fixture(owner, grantee, elderly);
    }

    private static FamiliesDbContext CreateDb() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    internal sealed record Fixture(UserId Owner, UserId Grantee, Elderly Elderly);

    internal sealed class FakeGateway(bool active) : IMedicalCaregiverGateway
    {
        public Task<bool> IsActiveMedicalCaregiverAsync(UserId userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(active);

        public Task<MedicalCaregiverRecipientPage> SearchActiveMedicalCaregiversAsync(
            string? search, int page, int pageSize, CancellationToken cancellationToken = default) =>
            Task.FromResult(new MedicalCaregiverRecipientPage([], 0));
    }
}






public sealed class E03GrantAuditTests
{
    private static FamiliesDbContext CreateDb() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MedicalAccessGrantHandlerTests.Fixture AddFixture(FamiliesDbContext db)
    {
        var owner = UserId.New();
        var grantee = UserId.New();
        var family = Family.Create(owner, "Grant tests");
        var elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father,
            FullName.Create("Test"), FullName.Create("Test"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();
        return new MedicalAccessGrantHandlerTests.Fixture(owner, grantee, elderly);
    }

    private static CreateMedicalAccessGrantCommand Command(MedicalAccessGrantHandlerTests.Fixture f) => new(
        f.Elderly.Id, f.Owner, f.Grantee, MedicalAccessGrantType.Limited,
        true, false, false, DateTime.UtcNow.AddDays(30), "test");

    [Fact]
    public async Task Create_Success_WritesAllowedAudit()
    {
        await using var db = CreateDb();
        var fx = AddFixture(db);
        var handler = new CreateMedicalAccessGrantCommandHandler(db, new MedicalAccessGrantHandlerTests.FakeGateway(true));
        var result = await handler.Handle(Command(fx), default);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, await db.MedicalAccessGrants.CountAsync());
        var audit = await db.CaregiverMedicalAccessAudits.SingleAsync();
        Assert.Equal(CaregiverMedicalAccessAuditDecision.Allowed, audit.Decision);
        Assert.Equal(CaregiverMedicalAccessAuditAction.CreateGrant, audit.Action);
        Assert.Equal(fx.Owner, audit.ActorUserId);
        Assert.Equal(fx.Elderly.Id, audit.DependentId);
        Assert.Equal(fx.Grantee, audit.GranteeUserId);
        Assert.Equal(result.Value.Id, audit.GrantId);
        Assert.Equal(MedicalAccessGrantType.Limited, audit.GrantType);
        Assert.True(audit.CanViewRecords);
        Assert.False(audit.CanEditRecords);
        Assert.False(audit.CanShareWithOthers);
        Assert.Null(audit.ReasonCode);
        Assert.Equal(DateTimeKind.Utc, audit.OccurredOnUtc.Kind);
        Assert.False(string.IsNullOrWhiteSpace(audit.CorrelationId));
    }

    [Fact]
    public async Task Revoke_Success_WritesAllowedAuditAndPersistsRevocation()
    {
        await using var db = CreateDb();
        var fx = AddFixture(db);
        var create = new CreateMedicalAccessGrantCommandHandler(db, new MedicalAccessGrantHandlerTests.FakeGateway(true));
        var created = await create.Handle(Command(fx), default);
        Assert.True(created.IsSuccess);

        var revoke = new RevokeMedicalAccessGrantCommandHandler(db);
        var result = await revoke.Handle(new RevokeMedicalAccessGrantCommand(fx.Elderly.Id, created.Value.Id, fx.Owner), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, await db.CaregiverMedicalAccessAudits.CountAsync());
        var audit = await db.CaregiverMedicalAccessAudits.SingleAsync(x => x.Action == CaregiverMedicalAccessAuditAction.RevokeGrant);
        Assert.Equal(CaregiverMedicalAccessAuditDecision.Allowed, audit.Decision);
        Assert.Equal(fx.Owner, audit.ActorUserId);
        Assert.Equal(fx.Elderly.Id, audit.DependentId);
        Assert.Equal(fx.Grantee, audit.GranteeUserId);
        Assert.Equal(created.Value.Id, audit.GrantId);
        var grant = await db.MedicalAccessGrants.SingleAsync();
        Assert.Equal(fx.Owner, grant.RevokedByUserId);
        Assert.NotNull(grant.RevokedOnUtc);
    }

    [Fact]
    public async Task Create_ForeignActor_DeniedAuditDoesNotCreateGrant()
    {
        await using var db = CreateDb();
        var fx = AddFixture(db);
        UserId otherActor = UserId.New();
        var command = Command(fx) with { GrantedByUserId = otherActor };
        var handler = new CreateMedicalAccessGrantCommandHandler(db, new MedicalAccessGrantHandlerTests.FakeGateway(true));

        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.AccessDenied", result.Error.Code);
        Assert.Empty(db.MedicalAccessGrants);
        var audit = await db.CaregiverMedicalAccessAudits.SingleAsync();
        Assert.Equal(CaregiverMedicalAccessAuditDecision.Denied, audit.Decision);
        Assert.Equal(CaregiverMedicalAccessAuditAction.CreateGrant, audit.Action);
        Assert.Equal(otherActor, audit.ActorUserId);
        Assert.Equal(fx.Elderly.Id, audit.DependentId);
        Assert.Equal("Families.AccessDenied", audit.ReasonCode);
        Assert.Null(audit.GranteeUserId);
        Assert.Null(audit.GrantId);
        Assert.Null(audit.GrantType);
        Assert.Null(audit.CanViewRecords);
        Assert.Null(audit.CanEditRecords);
        Assert.Null(audit.CanShareWithOthers);
        Assert.Null(audit.ExpiresOnUtc);
    }

    [Fact]
    public async Task Revoke_ForeignActor_DeniedAuditLeavesGrantUnrevoked()
    {
        await using var db = CreateDb();
        var fx = AddFixture(db);
        var create = new CreateMedicalAccessGrantCommandHandler(db, new MedicalAccessGrantHandlerTests.FakeGateway(true));
        var created = await create.Handle(Command(fx), default);
        Assert.True(created.IsSuccess);
        UserId otherActor = UserId.New();

        var revoke = new RevokeMedicalAccessGrantCommandHandler(db);
        var result = await revoke.Handle(new RevokeMedicalAccessGrantCommand(fx.Elderly.Id, created.Value.Id, otherActor), default);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.AccessDenied", result.Error.Code);
        Assert.Null((await db.MedicalAccessGrants.SingleAsync()).RevokedOnUtc);
        var denied = await db.CaregiverMedicalAccessAudits.SingleAsync(x => x.Decision == CaregiverMedicalAccessAuditDecision.Denied);
        Assert.Equal(CaregiverMedicalAccessAuditAction.RevokeGrant, denied.Action);
        Assert.Equal("Families.AccessDenied", denied.ReasonCode);
        Assert.Null(denied.GranteeUserId);
        Assert.Null(denied.GrantId);
    }

    [Fact]
    public async Task EmptyDependent_PreservesDenial_NoAudit()
    {
        await using var db = CreateDb();
        var fx = AddFixture(db);
        var cmd = new CreateMedicalAccessGrantCommand(ElderlyId.Empty, fx.Owner, fx.Grantee, MedicalAccessGrantType.Limited, true, false, false, DateTime.UtcNow.AddDays(30), "test");
        var handler = new CreateMedicalAccessGrantCommandHandler(db, new MedicalAccessGrantHandlerTests.FakeGateway(true));
        var result = await handler.Handle(cmd, default);
        Assert.True(result.IsFailure);
        Assert.Equal("Families.AccessDenied", result.Error.Code);
        Assert.Equal(0, await db.CaregiverMedicalAccessAudits.CountAsync());
    }

    [Fact]
    public async Task Revoke_UnknownGrantDoesNotWriteAudit()
    {
        await using var db = CreateDb();
        var fx = AddFixture(db);
        var handler = new RevokeMedicalAccessGrantCommandHandler(db);

        var result = await handler.Handle(new RevokeMedicalAccessGrantCommand(fx.Elderly.Id, MedicalAccessGrantId.New(), fx.Owner), default);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.GrantNotFound", result.Error.Code);
        Assert.Empty(db.CaregiverMedicalAccessAudits);
    }

    [Fact]
    public async Task Create_InvalidPermissionShapeDoesNotWriteAudit()
    {
        await using var db = CreateDb();
        var fx = AddFixture(db);
        var command = new CreateMedicalAccessGrantCommand(
            fx.Elderly.Id, fx.Owner, fx.Grantee, MedicalAccessGrantType.Limited,
            false, true, false,
            DateTime.UtcNow.AddDays(30), "invalid shape");
        var handler = new CreateMedicalAccessGrantCommandHandler(db, new MedicalAccessGrantHandlerTests.FakeGateway(true));

        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.InvalidGrant", result.Error.Code);
        Assert.Empty(db.CaregiverMedicalAccessAudits);
        Assert.Empty(db.MedicalAccessGrants);
    }
}




public sealed class E03PersistenceFailureTests
{
    private static FamiliesDbContext CreateDb() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MedicalAccessGrantHandlerTests.Fixture AddFixture(FamiliesDbContext db)
    {
        var owner = UserId.New();
        var grantee = UserId.New();
        var family = Family.Create(owner, "Grant tests");
        var elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father,
            FullName.Create("Test"), FullName.Create("Test"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();
        return new MedicalAccessGrantHandlerTests.Fixture(owner, grantee, elderly);
    }

    private sealed class ThrowingDb(IFamiliesDbContext inner, Exception toThrow) : FamiliesDbContextAdapter(inner)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw toThrow;
    }

    [Fact]
    public async Task Create_AuditSaveFailure_ReturnsAuditWriteFailed()
    {
        await using var db = CreateDb();
        var fx = AddFixture(db);
        var inner = db;
        var throwing = new ThrowingDb(inner, new DbUpdateException("boom"));
        // seed dependent via inner already in throwing's inner
        var cmd = new CreateMedicalAccessGrantCommand(fx.Elderly.Id, fx.Owner, fx.Grantee, MedicalAccessGrantType.Limited, true, false, false, DateTime.UtcNow.AddDays(30), "test");
        var handler = new CreateMedicalAccessGrantCommandHandler(throwing, new MedicalAccessGrantHandlerTests.FakeGateway(true));
        var result = await handler.Handle(cmd, default);
        Assert.True(result.IsFailure);
        Assert.Equal("Families.AuditWriteFailed", result.Error.Code);
    }

    [Fact]
    public async Task Create_Cancellation_Propagates()
    {
        await using var db = CreateDb();
        var fx = AddFixture(db);
        var throwing = new ThrowingDb(db, new OperationCanceledException());
        var cmd = new CreateMedicalAccessGrantCommand(fx.Elderly.Id, fx.Owner, fx.Grantee, MedicalAccessGrantType.Limited, true, false, false, DateTime.UtcNow.AddDays(30), "test");
        var handler = new CreateMedicalAccessGrantCommandHandler(throwing, new MedicalAccessGrantHandlerTests.FakeGateway(true));
        await Assert.ThrowsAsync<OperationCanceledException>(() => handler.Handle(cmd, default));
    }
}

public sealed class E03RelationalAtomicityTests
{
    private static MedicalAccessGrantHandlerTests.Fixture AddFixture(FamiliesDbContext db)
    {
        var owner = UserId.New();
        var grantee = UserId.New();
        var family = Family.Create(owner, "SQLite grant audit tests");
        var elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father,
            FullName.Create("Test"), FullName.Create("Test"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();
        return new MedicalAccessGrantHandlerTests.Fixture(owner, grantee, elderly);
    }

    private static CreateMedicalAccessGrantCommand Command(MedicalAccessGrantHandlerTests.Fixture fixture) => new(
        fixture.Elderly.Id, fixture.Owner, fixture.Grantee, MedicalAccessGrantType.Limited,
        true, false, false, DateTime.UtcNow.AddDays(30), "test");

    private static async Task AddAuditFailureTrigger(FamiliesDbContext db) =>
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_caregiver_audit_insert BEFORE INSERT ON caregiver_medical_access_audits " +
            "BEGIN SELECT RAISE(ABORT, 'test audit insert failure'); END;");

    [Fact]
    public async Task Create_AuditInsertFailure_RollsBackGrantAndAudit()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FamiliesDbContext>().UseSqlite(connection).Options;
        UserId owner;
        UserId grantee;
        ElderlyId dependentId;

        await using (var db = new FamiliesDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var seeded = AddFixture(db);
            owner = seeded.Owner;
            grantee = seeded.Grantee;
            dependentId = seeded.Elderly.Id;
            await AddAuditFailureTrigger(db);

            var handler = new CreateMedicalAccessGrantCommandHandler(
                db,
                new MedicalAccessGrantHandlerTests.FakeGateway(true));
            var result = await handler.Handle(
                new CreateMedicalAccessGrantCommand(
                    dependentId, owner, grantee, MedicalAccessGrantType.Limited,
                    true, false, false, DateTime.UtcNow.AddDays(30), "test"),
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Families.AuditWriteFailed", result.Error.Code);
        }

        await using var verify = new FamiliesDbContext(options);
        Assert.Empty(await verify.MedicalAccessGrants.ToListAsync());
        Assert.Empty(await verify.CaregiverMedicalAccessAudits.ToListAsync());
    }

    [Fact]
    public async Task Revoke_AuditInsertFailure_RollsBackRevocationAndAudit()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FamiliesDbContext>().UseSqlite(connection).Options;
        Guid grantId;
        UserId owner;
        ElderlyId dependentId;

        await using (var db = new FamiliesDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var fixture = AddFixture(db);
            owner = fixture.Owner;
            dependentId = fixture.Elderly.Id;
            var create = new CreateMedicalAccessGrantCommandHandler(
                db,
                new MedicalAccessGrantHandlerTests.FakeGateway(true));
            var created = await create.Handle(Command(fixture), CancellationToken.None);
            Assert.True(created.IsSuccess);
            grantId = created.Value.Id.Value;
            await AddAuditFailureTrigger(db);

            var revoke = new RevokeMedicalAccessGrantCommandHandler(db);
            var result = await revoke.Handle(
                new RevokeMedicalAccessGrantCommand(
                    dependentId, new MedicalAccessGrantId(grantId), owner),
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Families.AuditWriteFailed", result.Error.Code);
        }

        await using var verify = new FamiliesDbContext(options);
        MedicalAccessGrant grant = await verify.MedicalAccessGrants.SingleAsync();
        Assert.Null(grant.RevokedOnUtc);
        Assert.Null(grant.RevokedByUserId);
        Assert.Equal(1, await verify.CaregiverMedicalAccessAudits.CountAsync());
    }

    [Fact]
    public async Task DeniedAuditInsertFailure_ReturnsAuditWriteFailure()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<FamiliesDbContext>().UseSqlite(connection).Options;
        UserId owner;
        UserId grantee;
        ElderlyId dependentId;

        await using (var db = new FamiliesDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var fixture = AddFixture(db);
            owner = fixture.Owner;
            grantee = fixture.Grantee;
            dependentId = fixture.Elderly.Id;
            await AddAuditFailureTrigger(db);

            var handler = new CreateMedicalAccessGrantCommandHandler(
                db,
                new MedicalAccessGrantHandlerTests.FakeGateway(true));
            var result = await handler.Handle(
                new CreateMedicalAccessGrantCommand(
                    dependentId, UserId.New(), grantee, MedicalAccessGrantType.Limited,
                    true, false, false, DateTime.UtcNow.AddDays(30), "test"),
                CancellationToken.None);

            Assert.True(result.IsFailure);
            Assert.Equal("Families.AuditWriteFailed", result.Error.Code);
        }

        await using var verify = new FamiliesDbContext(options);
        Assert.Empty(await verify.MedicalAccessGrants.ToListAsync());
        Assert.Empty(await verify.CaregiverMedicalAccessAudits.ToListAsync());
    }
}

public sealed class E03GrantControllerFailureTests
{
    [Fact]
    public async Task CreateAndRevoke_AuditWriteFailureReturnSafe503()
    {
        UserId actor = UserId.New();
        var error = new Error("Families.AuditWriteFailed", "The medical access audit could not be recorded.");
        var sender = new AuditFailureSender(
            Result<MedicalAccessGrant>.Failure(error),
            Result<bool>.Failure(error));
        var controller = new DependentMedicalAccessGrantsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(JwtRegisteredClaimNames.Sub, actor.Value.ToString())
                    ],
                    "test"))
                }
            }
        };

        var create = await controller.CreateMedicalAccessGrant(
            Guid.NewGuid(),
            new CreateMedicalAccessGrantRequest(
                "Limited", true, false, false, Guid.NewGuid(), DateTime.UtcNow.AddDays(1), null),
            CancellationToken.None);
        var createFailure = Assert.IsType<ObjectResult>(create);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, createFailure.StatusCode);
        Assert.DoesNotContain("database", createFailure.Value!.ToString(), StringComparison.OrdinalIgnoreCase);

        var revoke = await controller.DeleteMedicalAccessGrant(
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        var revokeFailure = Assert.IsType<ObjectResult>(revoke);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, revokeFailure.StatusCode);
        Assert.DoesNotContain("database", revokeFailure.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AuditFailureSender(
        object createResult,
        object revokeResult) : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                CreateMedicalAccessGrantCommand => createResult,
                RevokeMedicalAccessGrantCommand => revokeResult,
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
}
