using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Application.Abstractions.Identity;
using Ahlena.Modules.Families.Application.CheckIns;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Elderlies.CheckIns;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Families.CheckIns;

public sealed class AdminMissedCheckInQueryTests
{
    private sealed class FakeClock(DateTime utcNow) : IDateTimeProvider { public DateTime UtcNow { get; } = utcNow; }
    private sealed class RejectAudit : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Synthetic audit storage failure");
    }

    private sealed class FakeGateway(Dictionary<UserId, int> statuses) : IFamilyIdentityGateway
    {
        public bool ThrowOnRead { get; init; }
        public int Calls { get; private set; }
        public Task<IReadOnlyList<FamilyMemberProfile>> GetFamilyMemberProfilesAsync(IReadOnlyCollection<UserId> userIds, CancellationToken ct = default)
        {
            Calls++;
            if (ThrowOnRead) throw new InvalidOperationException("Synthetic source unavailable");
            var list = userIds
                .Where(id => statuses.ContainsKey(id))
                .Select(id => new FamilyMemberProfile(id, "a", "e", null, Status: statuses[id]))
                .ToList();
            // If any requested id missing from statuses, return only present (simulates unresolvable) unless all present.
            if (statuses.Count == 0) list = [];
            return Task.FromResult<IReadOnlyList<FamilyMemberProfile>>(list);
        }

        public Task<Result<ElderlyIdentityAccount>> GetElderlyByPhoneAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<ElderlyIdentityAccount>> CreateElderlyAsync(string arabicFullName, string englishFullName, string phoneNumber, Gender gender, DateOnly dateOfBirth, DateTime utcNow, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteElderlyAsync(UserId userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Result<FamilyInviteeAccount>> GetFamilyInviteeByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SendFamilyInvitationEmailAsync(string email, string familyName, string invitationToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Result> AnonymizeFamilyAccountsAsync(IReadOnlyCollection<UserId> userIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static FamiliesDbContext CreateDb(out UserId actor, out Family family, out Elderly elderly, out UserId identityUserId, DateTime? createdUtc = null, string timeZoneId = "Africa/Cairo")
    {
        var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        actor = UserId.New();
        var owner = UserId.New();
        identityUserId = UserId.New();
        family = Family.Create(owner);
        elderly = Elderly.Create(owner, identityUserId, family.Id, FamilyRelationshipType.Father,
            FullName.Create("الاسم"), FullName.Create("Elderly"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2026, 1, 1));
        elderly.ChangeTimeZone(timeZoneId);
        db.AddRange(family, elderly);
        db.SaveChanges();
        if (createdUtc.HasValue)
        {
            typeof(Elderly).GetProperty(nameof(Elderly.CreatedOnUtc))!.SetValue(elderly, createdUtc.Value);
            db.SaveChanges();
        }
        return db;
    }

    [Fact]
    public async Task FailedAudit_StopsBeforeIdentityRead()
    {
        await using var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(new RejectAudit()).Options);
        var gateway = new FakeGateway(new());
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(new DateTime(2026,9,2,0,0,0,DateTimeKind.Utc)));
        var result = await handler.Handle(new(UserId.New(),"SupportAdmin","audit-failure",new(2026,1,2),new(2026,1,3)), CancellationToken.None);
        Assert.Equal("Families.AdminCheckIn.EvaluationUnavailable", result.Error.Code);
        Assert.Equal(0, gateway.Calls);
        Assert.Empty(await db.AdminMedicationAccessAudits.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ObservableIdentityFailure_IsUnavailableAfterAudit()
    {
        await using var db = CreateDb(out var actor, out _, out _, out _, new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc));
        var handler = new GetAdminMissedCheckInsQueryHandler(db, new FakeGateway(new()) { ThrowOnRead = true }, new FakeClock(DateTime.UtcNow));
        var result = await handler.Handle(new(actor,"SupportAdmin","source-failure",new(2026,1,2),new(2026,1,3)), CancellationToken.None);
        Assert.Equal("Families.AdminCheckIn.EvaluationUnavailable", result.Error.Code);
        Assert.Single(await db.AdminMedicationAccessAudits.ToListAsync());
    }

    [Fact]
    public async Task EmptyCohort_ReturnsEmptyPage()
    {
        await using var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var actor = UserId.New();
        var handler = new GetAdminMissedCheckInsQueryHandler(db, new FakeGateway(new()), new FakeClock(new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc)));
        var result = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2)), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(0, result.Value.TotalCount);
    }

    [Fact]
    public async Task MissingIdentityProfile_IsIndeterminate()
    {
        var db = CreateDb(out var actor, out _, out var elderly, out var identityUserId, new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc));
        var gateway = new FakeGateway(new Dictionary<UserId, int>()); // missing
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)));
        var result = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2)), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("Families.AdminCheckIn.IndeterminateEvaluation", result.Error.Code);
    }

    [Fact]
    public async Task InactiveIdentity_Excluded_NotIndeterminate()
    {
        var db = CreateDb(out var actor, out _, out var elderly, out var identityUserId, new DateTime(2026, 8, 31, 20, 0, 0, DateTimeKind.Utc));
        var gateway = new FakeGateway(new Dictionary<UserId, int> { [identityUserId] = 3 }); // suspended
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)));
        var result = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 2)), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task CreationDayExcluded_FirstFullDayIncluded()
    {
        // Created on 2026-09-01 local (Cairo) => first expected 2026-09-02
        var createdUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc); // Cairo +2? local Sep1 02:00
        var db = CreateDb(out var actor, out _, out var elderly, out var identityUserId, createdUtc);
        var gateway = new FakeGateway(new Dictionary<UserId, int> { [identityUserId] = 2 });
        var eval = new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc);
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(eval));
        var result = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 3)), CancellationToken.None);
        Assert.True(result.IsSuccess);
        // Sep1 excluded (creation day), Sep2 included if due, Sep3 maybe not due depending eval
        Assert.DoesNotContain(result.Value.Items, x => x.LocalDate == new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task ExactDeadline_Due()
    {
        var createdUtc = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var db = CreateDb(out var actor, out _, out var elderly, out var identityUserId, createdUtc);
        var gateway = new FakeGateway(new Dictionary<UserId, int> { [identityUserId] = 2 });
        var deadlineUtc = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 9, 2), TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"));
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(deadlineUtc));
        var result = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
    }

    [Fact]
    public async Task LateAnswer_RemovesRow()
    {
        var createdUtc = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var db = CreateDb(out var actor, out _, out var elderly, out var identityUserId, createdUtc);
        var checkIn = ElderlyCheckIn.Create(elderly.Id, new DateOnly(2026, 9, 1), true, new TimeOnly(8, 0), new DateTime(2026, 9, 2, 6, 0, 0, DateTimeKind.Utc), actor);
        db.ElderlyCheckIns.Add(checkIn);
        db.SaveChanges();
        var gateway = new FakeGateway(new Dictionary<UserId, int> { [identityUserId] = 2 });
        var eval = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(eval));
        var result = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task PostEvaluationAnswer_NotCounted()
    {
        var createdUtc = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var db = CreateDb(out var actor, out _, out var elderly, out var identityUserId, createdUtc);
        var checkIn = ElderlyCheckIn.Create(elderly.Id, new DateOnly(2026, 9, 1), true, new TimeOnly(8, 0), new DateTime(2026, 9, 5, 6, 0, 0, DateTimeKind.Utc), actor);
        db.ElderlyCheckIns.Add(checkIn);
        db.SaveChanges();
        var gateway = new FakeGateway(new Dictionary<UserId, int> { [identityUserId] = 2 });
        var eval = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc);
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(eval));
        var result = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
    }

    [Fact]
    public async Task DeletedFamily_Excluded()
    {
        var db = CreateDb(out var actor, out var family, out var elderly, out var identityUserId, new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        family.MarkDeleted("request", null);
        db.SaveChanges();
        var gateway = new FakeGateway(new Dictionary<UserId, int> { [identityUserId] = 2 });
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)));
        var result = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task InvalidRange_RejectedBeforeAudit()
    {
        var db = CreateDb(out var actor, out _, out _, out _, new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        var gateway = new FakeGateway(new Dictionary<UserId, int>());
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)));
        var result = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 1)), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("Families.AdminCheckIn.InvalidDateRange", result.Error.Code);
        Assert.Equal(0, await db.AdminMedicationAccessAudits.CountAsync());
    }

    [Fact]
    public async Task Audit_BeforeRead()
    {
        var db = CreateDb(out var actor, out _, out _, out var identityUserId, new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc));
        var gateway = new FakeGateway(new Dictionary<UserId, int> { [identityUserId] = 2 });
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)));
        await handler.Handle(new(actor, "SupportAdmin", "corr", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1)), CancellationToken.None);
        var audit = Assert.Single(await db.AdminMedicationAccessAudits.ToListAsync());
        Assert.Equal("ListMissedCheckIns", audit.Action);
        Assert.Equal("corr", audit.CorrelationId);
    }

    [Fact]
    public async Task MinimalDto_NoSensitiveFields()
    {
        var props = string.Join(',', typeof(MissedCheckInItem).GetProperties().Select(p => p.Name));
        Assert.DoesNotContain("Name", props);
        Assert.DoesNotContain("Phone", props);
        Assert.DoesNotContain("TimeZone", props);
        Assert.DoesNotContain("Clinical", props);
        var pagedProps = string.Join(',', typeof(PagedMissedCheckIns).GetProperties().Select(p => p.Name));
        Assert.Contains("EvaluationOnUtc", pagedProps);
        Assert.Contains("Items", pagedProps);
        Assert.Contains("Page", pagedProps);
        Assert.Contains("PageSize", pagedProps);
        Assert.Contains("TotalCount", pagedProps);
    }

    [Theory]
    [InlineData("America/Havana", 2026, 11, 1)]
    [InlineData("America/Havana", 2026, 3, 8)]
    public async Task ExceptionalMidnight_IsIndeterminate(string zoneId, int year, int month, int day)
    {
        var boundary = new DateTime(year, month, day);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        Assert.True(zone.IsAmbiguousTime(boundary) || zone.IsInvalidTime(boundary));
        await using var db = CreateDb(out var actor, out _, out _, out var user,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), zoneId);
        var handler = new GetAdminMissedCheckInsQueryHandler(db, new FakeGateway(new() { [user] = 2 }),
            new FakeClock(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)));
        var date = DateOnly.FromDateTime(boundary).AddDays(-1);
        var result = await handler.Handle(new(actor, "SupportAdmin", "dst", date, date), CancellationToken.None);
        Assert.Equal("Families.AdminCheckIn.IndeterminateEvaluation", result.Error.Code);
    }

    [Fact]
    public async Task BeforeDeadline_IsNotYetDue()
    {
        await using var db = CreateDb(out var actor, out _, out _, out var user,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var deadline = TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 9, 2), TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"));
        var handler = new GetAdminMissedCheckInsQueryHandler(db, new FakeGateway(new() { [user] = 2 }), new FakeClock(deadline.AddTicks(-1)));
        var result = await handler.Handle(new(actor, "SupportAdmin", "before", new(2026,9,1), new(2026,9,1)), CancellationToken.None);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task Paging_Stable()
    {
        var createdUtc = new DateTime(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);
        var db = CreateDb(out var actor, out _, out _, out var identityUserId, createdUtc);
        // two elderlies? use same db single. Add second elderly manually
        var owner = UserId.New();
        var id2 = UserId.New();
        var family2 = Family.Create(owner);
        var elderly2 = Elderly.Create(owner, id2, family2.Id, FamilyRelationshipType.Father, FullName.Create("الاسم"), FullName.Create("E2"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2026, 1, 1));
        elderly2.ChangeTimeZone("Africa/Cairo");
        typeof(Elderly).GetProperty(nameof(Elderly.CreatedOnUtc))!.SetValue(elderly2, createdUtc);
        db.Families.Add(family2);
        db.Elderlies.Add(elderly2);
        db.SaveChanges();
        var statuses = new Dictionary<UserId, int> { [identityUserId] = 2, [id2] = 2 };
        var gateway = new FakeGateway(statuses);
        var handler = new GetAdminMissedCheckInsQueryHandler(db, gateway, new FakeClock(new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)));
        var r1 = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), 1, 1), CancellationToken.None);
        var r2 = await handler.Handle(new(actor, "SupportAdmin", "c", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 1), 2, 1), CancellationToken.None);
        Assert.Equal(2, r1.Value.TotalCount);
        Assert.Single(r1.Value.Items);
        Assert.Single(r2.Value.Items);
        Assert.NotEqual(r1.Value.Items[0].ElderlyId, r2.Value.Items[0].ElderlyId);
    }
}
