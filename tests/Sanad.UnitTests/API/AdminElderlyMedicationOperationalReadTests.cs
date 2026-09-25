using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Medications;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

public sealed class AdminElderlyMedicationOperationalReadTests
{
    [Fact]
    public void Controller_UsesOperationalReadPolicy_AndExposesApprovedRoutes()
    {
        var authorization = Assert.Single(typeof(AdminElderlyMedicationsController)
            .GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.ElderlyMedicationOperationalRead, authorization.Policy);
        Assert.Equal("api/v1/admin/elderly/medications", Assert.Single(typeof(AdminElderlyMedicationsController).GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("{medicationId:guid}/doses", typeof(AdminElderlyMedicationsController).GetMethod(nameof(AdminElderlyMedicationsController.Doses))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("adherence", typeof(AdminElderlyMedicationsController).GetMethod(nameof(AdminElderlyMedicationsController.Adherence))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
    }

    [Fact]
    public void OperationalReadPolicy_RequiresNormalAccess_AndOnlySuperOrSupportAdmin()
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(AuthClaimNames.AccessType, AuthAccessType.Normal.ToString())
            .RequireClaim(AuthClaimNames.AccountType, AccountType.SuperAdmin.ToString(), AccountType.SupportAdmin.ToString())
            .Build();

        foreach (var accountType in new[] { AccountType.SuperAdmin, AccountType.SupportAdmin })
            Assert.True(policy.Requirements.All(requirement => Satisfies(requirement, accountType, AuthAccessType.Normal)));

        foreach (var accountType in new[] { AccountType.ContentAdmin, AccountType.Family, AccountType.Elderly })
            Assert.False(policy.Requirements.All(requirement => Satisfies(requirement, accountType, AuthAccessType.Normal)));
        Assert.False(policy.Requirements.All(requirement => Satisfies(requirement, AccountType.SuperAdmin, AuthAccessType.RestrictedVerification)));
    }

    [Fact]
    public async Task List_AppliesDependentStatusSearchPaging_AndAuditsBeforeReturning()
    {
        using var db = CreateFixture(out var actor, out var elderly, out var medication, out var otherMedication);
        var result = await new GetAdminMedicationsQueryHandler(db).Handle(
            new(actor, AccountType.SupportAdmin.ToString(), "req-list", 2, 1, elderly.Id.Value, MedicationStatus.Active, "asp"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items); // one matching item is on page one
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(1, result.Value.PageSize);
        Assert.Equal(1, result.Value.TotalCount);
        var audit = Assert.Single(await db.AdminMedicationAccessAudits.ToListAsync());
        Assert.Equal(actor, audit.ActorUserId);
        Assert.Equal(AccountType.SupportAdmin.ToString(), audit.ActorAccountType);
        Assert.Equal("ListMedications", audit.Action);
        Assert.Equal("req-list", audit.CorrelationId);
        Assert.Null(audit.ResourceId);
        Assert.NotEqual(medication.Id, otherMedication.Id);
    }

    [Fact]
    public async Task List_NormalizesInvalidPaging_AndReturnsMatchingItem()
    {
        using var db = CreateFixture(out var actor, out var elderly, out var medication, out _);
        var result = await new GetAdminMedicationsQueryHandler(db).Handle(
            new(actor, AccountType.SuperAdmin.ToString(), "req-page", 0, 0, null, null, "ASPIRIN"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(medication.Id.Value, item.Medication.Id);
        Assert.Equal(elderly.FamilyId.Value, item.FamilyId);
        Assert.Equal("Ahmed", item.ElderlyArabicName);
        Assert.Equal("Ahmed", item.ElderlyEnglishName);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(20, result.Value.PageSize);
    }

    [Fact]
    public async Task List_WithMaximumPageNumber_ReturnsEmptyPageWithoutOffsetOverflow()
    {
        using var db = CreateFixture(out var actor, out _, out _, out _);

        var result = await new GetAdminMedicationsQueryHandler(db).Handle(
            new(actor, AccountType.SupportAdmin.ToString(), "req-large-page", int.MaxValue, 100),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(int.MaxValue, result.Value.Page);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Single(await db.AdminMedicationAccessAudits.ToListAsync());
    }

    [Fact]
    public async Task Detail_IsNotFoundForMissingMedication_AndStillPersistsAudit()
    {
        using var db = CreateFixture(out var actor, out _, out _, out _);
        var missing = MedicationId.New();
        var result = await new GetAdminMedicationQueryHandler(db).Handle(
            new(actor, AccountType.SupportAdmin.ToString(), "req-detail", missing), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Families.AdminMedication.NotFound", result.Error.Code);
        var audit = Assert.Single(await db.AdminMedicationAccessAudits.ToListAsync());
        Assert.Equal("GetMedication", audit.Action);
        Assert.Equal(missing.Value, audit.ResourceId);
    }

    [Fact]
    public async Task Detail_IncludesLinkedElderlyNamesAndFamilyContext()
    {
        using var db = CreateFixture(out var actor, out var elderly, out var medication, out _);

        var result = await new GetAdminMedicationQueryHandler(db).Handle(
            new(actor, AccountType.SupportAdmin.ToString(), "req-context", medication.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(medication.Id.Value, result.Value.Medication.Id);
        Assert.Equal(elderly.Id.Value, result.Value.Medication.DependentId);
        Assert.Equal(elderly.FamilyId.Value, result.Value.FamilyId);
        Assert.Equal("Ahmed", result.Value.ElderlyArabicName);
        Assert.Equal("Ahmed", result.Value.ElderlyEnglishName);
    }

    [Fact]
    public async Task DoseTimeline_IsInclusive_ProjectsDateStatusAndActor_AndFiltersStatus()
    {
        using var db = CreateFixture(out var actor, out var elderly, out var medication, out _);
        var start = new DateOnly(2026, 9, 1);
        var end = new DateOnly(2026, 9, 3);
        var first = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, start, new TimeOnly(8, 0));
        var middle = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, new DateOnly(2026, 9, 2), new TimeOnly(9, 0));
        middle.MarkAsTaken(actor, new DateTime(2026, 9, 2, 9, 1, 0, DateTimeKind.Utc), "taken");
        var last = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, end, new TimeOnly(10, 0));
        last.MarkAsSkipped(actor, new DateTime(2026, 9, 3, 10, 1, 0, DateTimeKind.Utc), "missed");
        db.MedicationDoseLogs.AddRange(first, middle, last);
        await db.SaveChangesAsync();

        var result = await new GetAdminMedicationDoseTimelineQueryHandler(db).Handle(
            new(actor, AccountType.SupportAdmin.ToString(), "req-doses", medication.Id, start, end, DoseStatus.Taken), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var dose = Assert.Single(result.Value);
        Assert.Equal(new DateOnly(2026, 9, 2), dose.ScheduledDate);
        Assert.Equal(new TimeOnly(9, 0), dose.ScheduledTime);
        Assert.Equal(DoseStatus.Taken, dose.Status);
        Assert.Equal(actor.Value, dose.LoggedByUserId);
        Assert.Equal("req-doses", Assert.Single(await db.AdminMedicationAccessAudits.Where(x => x.Action == "GetMedicationDoseTimeline").ToListAsync()).CorrelationId);
    }

    [Theory]
    [InlineData(2026, 9, 3, 2026, 9, 2)]
    [InlineData(2026, 9, 1, 2026, 10, 2)]
    public async Task DoseTimeline_RejectsReversedOrOver31DayRangeWithoutAudit(int sy, int sm, int sd, int ey, int em, int ed)
    {
        using var db = CreateFixture(out var actor, out _, out var medication, out _);
        var result = await new GetAdminMedicationDoseTimelineQueryHandler(db).Handle(
            new(actor, AccountType.SuperAdmin.ToString(), "req-invalid", medication.Id,
                new DateOnly(sy, sm, sd), new DateOnly(ey, em, ed)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Families.AdminMedication.InvalidDateRange", result.Error.Code);
        Assert.Empty(await db.AdminMedicationAccessAudits.ToListAsync());
    }

    [Fact]
    public async Task DoseTimeline_AllowsExactly31CalendarDays()
    {
        using var db = CreateFixture(out var actor, out _, out var medication, out _);
        var result = await new GetAdminMedicationDoseTimelineQueryHandler(db).Handle(
            new(actor, AccountType.SuperAdmin.ToString(), "req-31", medication.Id,
                new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(await db.AdminMedicationAccessAudits.ToListAsync());
    }

    [Fact]
    public async Task Adherence_ReportsFilteredStatusCountsAndPercentage()
    {
        using var db = CreateFixture(out var actor, out var elderly, out var medication, out _);
        var date = new DateOnly(2026, 9, 5);
        var taken = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, date, new TimeOnly(8, 0));
        taken.MarkAsTaken(actor, DateTime.UtcNow);
        var skipped = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, date, new TimeOnly(9, 0));
        skipped.MarkAsSkipped(actor, DateTime.UtcNow);
        var scheduled = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, date, new TimeOnly(10, 0));
        db.MedicationDoseLogs.AddRange(taken, skipped, scheduled);
        await db.SaveChangesAsync();

        var result = await new GetAdminMedicationAdherenceQueryHandler(db).Handle(
            new(actor, AccountType.SupportAdmin.ToString(), "req-aggregate", date, date, elderly.Id.Value), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalDoses);
        Assert.Equal(1, result.Value.TakenDoses);
        Assert.Equal(1, result.Value.SkippedDoses);
        Assert.Equal(1, result.Value.ScheduledDoses);
        Assert.Equal(0, result.Value.MissedDoses);
        Assert.Equal(33.33m, result.Value.AdherenceRate);
    }

    [Fact]
    public async Task Audit_IsPersistedWithActorAccountActionResourceAndCorrelation_AndHasNoPublicMutationSetters()
    {
        using var db = CreateFixture(out var actor, out _, out var medication, out _);
        await new GetAdminMedicationQueryHandler(db).Handle(
            new(actor, AccountType.SuperAdmin.ToString(), "req-audit", medication.Id), CancellationToken.None);

        var audit = Assert.Single(await db.AdminMedicationAccessAudits.ToListAsync());
        Assert.Equal(actor, audit.ActorUserId);
        Assert.Equal(AccountType.SuperAdmin.ToString(), audit.ActorAccountType);
        Assert.Equal("GetMedication", audit.Action);
        Assert.Equal("Medication", audit.ResourceType);
        Assert.Equal(medication.Id.Value, audit.ResourceId);
        Assert.Equal("req-audit", audit.CorrelationId);
        Assert.NotEqual(default, audit.OccurredOnUtc);
        Assert.DoesNotContain(typeof(AdminMedicationAccessAudit).GetProperties(BindingFlags.Instance | BindingFlags.Public), property => property.SetMethod is not null && property.SetMethod.IsPublic);
    }

    private static bool Satisfies(IAuthorizationRequirement requirement, AccountType accountType, AuthAccessType accessType)
    {
        if (requirement is DenyAnonymousAuthorizationRequirement)
            return true;
        if (requirement is ClaimsAuthorizationRequirement claim)
        {
            var value = claim.ClaimType == AuthClaimNames.AccountType ? accountType.ToString() : accessType.ToString();
            return claim.AllowedValues?.Contains(value) == true;
        }
        return false;
    }

    private static FamiliesDbContext CreateFixture(
        out UserId actor, out Elderly elderly, out Medication medication, out Medication otherMedication)
    {
        var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        actor = UserId.New();
        var owner = UserId.New();
        var family = Family.Create(owner, "Medication operations");
        elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father,
            FullName.Create("Ahmed"), FullName.Create("Ahmed"), Gender.Male,
            new DateOnly(1955, 1, 1), new DateOnly(2020, 1, 1));
        medication = Medication.Create(elderly.Id, owner, "Aspirin", "100 mg", "tablet", 1,
            new[] { new TimeOnly(8, 0) }, new DateOnly(2026, 1, 1), null, "after food", 10, 2);
        otherMedication = Medication.Create(elderly.Id, owner, "Vitamin D", "1000 IU", "tablet", 1,
            new[] { new TimeOnly(12, 0) }, new DateOnly(2026, 1, 1), null, null, 10, 2);
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.Medications.AddRange(medication, otherMedication);
        db.SaveChanges();
        return db;
    }
}
