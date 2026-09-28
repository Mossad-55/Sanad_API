using System.Reflection;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.UnitTests.Notifications;

public sealed class FamilyNotificationRecipientGatewayTests
{
    [Fact]
    public async Task Gateway_ShouldScopeToActiveFamilyMembersAndDeduplicateResults()
    {
        await using var db = CreateDb();
        var owner = UserId.New(); var member = UserId.New(); var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "target");
        family.AddMember(FamilyMember.Create(member, owner, FamilyRelationshipType.Son, FamilyRole.Editor));
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father, FullName.Create("عمر"), FullName.Create("Elderly"), Gender.Male, new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        db.Families.Add(family); db.Elderlies.Add(elderly); await db.SaveChangesAsync();

        var sender = new RecipientSender();
        var caregivers = CreateCaregiversDb();
        var gateway = new Sanad.API.NotificationsIntegration.FamilyNotificationRecipientGateway(db, sender, caregivers);
        var result = await Invoke(gateway, elderlyIdentity, elderly.Id.Value);

        Assert.Equal(new[] { owner, member }, result);
        Assert.Equal(new[] { owner, member }, sender.LastIds);
    }

    [Fact]
    public async Task Gateway_ShouldReturnNoRecipientsForDeletedFamilyOrMismatchedElderly()
    {
        await using var db = CreateDb();
        var owner = UserId.New(); var elderlyIdentity = UserId.New();
        var family = Family.Create(owner); family.MarkDeleted("requested", null);
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father, FullName.Create("عمر"), FullName.Create("Elderly"), Gender.Male, new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        db.Families.Add(family); db.Elderlies.Add(elderly); await db.SaveChangesAsync();
        var sender = new RecipientSender();
        var gateway = new Sanad.API.NotificationsIntegration.FamilyNotificationRecipientGateway(db, sender, CreateCaregiversDb());
        Assert.Empty(await Invoke(gateway, elderlyIdentity, elderly.Id.Value));
        Assert.Empty(sender.LastIds!);
        Assert.Empty(await Invoke(gateway, UserId.New(), elderly.Id.Value));
    }

    [Fact]
    public async Task MedicationGateway_IncludesActiveFamilyAssignedActiveBookingAndSupportAdmin_ButIsolatesFamilies()
    {
        await using var families = CreateDb();
        await using var caregivers = CreateCaregiversDb();
        var owner = UserId.New();
        var familyMember = UserId.New();
        var foreignMember = UserId.New();
        var assignedCaregiverUser = UserId.New();
        var unassignedCaregiverUser = UserId.New();
        var admin = UserId.New();
        var family = Family.Create(owner, "target");
        family.AddMember(FamilyMember.Create(familyMember, owner, FamilyRelationshipType.Son, FamilyRole.Editor));
        var foreignFamily = Family.Create(UserId.New(), "foreign");
        foreignFamily.AddMember(FamilyMember.Create(foreignMember, owner, FamilyRelationshipType.Son, FamilyRole.Editor));
        var elderlyIdentity = UserId.New();
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("Ø¹Ù…Ø±"), FullName.Create("Elderly"), Gender.Male,
            new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        var foreignElderly = Elderly.Create(foreignFamily.OwnerUserId, UserId.New(), foreignFamily.Id,
            FamilyRelationshipType.Father, FullName.Create("Foreign"), FullName.Create("Foreign"), Gender.Male,
            new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        var assigned = Caregiver.Create(assignedCaregiverUser, CaregiverType.Companion);
        var unassigned = Caregiver.Create(unassignedCaregiverUser, CaregiverType.Companion);
        families.Families.AddRange(family, foreignFamily);
        families.Elderlies.AddRange(elderly, foreignElderly);
        caregivers.Caregivers.AddRange(assigned, unassigned);
        families.Bookings.Add(CreateBooking(family.Id, owner, elderly.Id, assigned.Id));
        var inactiveBooking = CreatePendingBooking(family.Id, owner, elderly.Id, unassigned.Id);
        inactiveBooking.MarkAsPaid("order", "transaction", DateTime.UtcNow);
        inactiveBooking.DeclineByCaregiver("not available", DateTime.UtcNow);
        families.Bookings.Add(inactiveBooking);
        await families.SaveChangesAsync();
        await caregivers.SaveChangesAsync();

        var sender = new MedicationRecipientSender(admin);
        var gateway = new Sanad.API.NotificationsIntegration.FamilyNotificationRecipientGateway(families, sender, caregivers);
        var result = await InvokeMedication(gateway, elderlyIdentity, elderly.Id.Value);

        Assert.Contains(owner, result);
        Assert.Contains(familyMember, result);
        Assert.Contains(assignedCaregiverUser, result);
        Assert.Contains(admin, result);
        Assert.DoesNotContain(unassignedCaregiverUser, result);
        Assert.DoesNotContain(foreignMember, result);
        Assert.DoesNotContain(foreignElderly.IdentityUserId, result);
    }

    [Fact]
    public async Task HelpRequestGateway_IncludesOnlyEnabledFamilyAndActiveAssignedCaregiversAndActiveSupportAdmins()
    {
        await using var families = CreateDb();
        await using var caregivers = CreateCaregiversDb();
        var owner = UserId.New();
        var disabledFamilyMember = UserId.New();
        var confirmedCaregiverUser = UserId.New();
        var inProgressCaregiverUser = UserId.New();
        var disabledCaregiverUser = UserId.New();
        var completedCaregiverUser = UserId.New();
        var cancelledCaregiverUser = UserId.New();
        var pendingCaregiverUser = UserId.New();
        var admin = UserId.New();
        var family = Family.Create(owner, "help-request-target");
        family.AddMember(FamilyMember.Create(disabledFamilyMember, owner, FamilyRelationshipType.Son, FamilyRole.Editor));
        var elderlyIdentity = UserId.New();
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("Family"), FullName.Create("Elderly"), Gender.Male,
            new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        var caregiversByUser = new[]
        {
            Caregiver.Create(confirmedCaregiverUser, CaregiverType.Companion),
            Caregiver.Create(inProgressCaregiverUser, CaregiverType.Companion),
            Caregiver.Create(disabledCaregiverUser, CaregiverType.Companion),
            Caregiver.Create(completedCaregiverUser, CaregiverType.Companion),
            Caregiver.Create(cancelledCaregiverUser, CaregiverType.Companion),
            Caregiver.Create(pendingCaregiverUser, CaregiverType.Companion)
        };
        families.Families.Add(family);
        families.Elderlies.Add(elderly);
        caregivers.Caregivers.AddRange(caregiversByUser);

        var confirmed = CreateBooking(family.Id, owner, elderly.Id, caregiversByUser[0].Id);
        var inProgress = CreateBooking(family.Id, owner, elderly.Id, caregiversByUser[1].Id);
        inProgress.StartVisit(DateTime.UtcNow.AddMinutes(2));
        var disabledPreferenceActive = CreateBooking(family.Id, owner, elderly.Id, caregiversByUser[2].Id);
        var completed = CreateBooking(family.Id, owner, elderly.Id, caregiversByUser[3].Id);
        completed.StartVisit(DateTime.UtcNow.AddMinutes(2));
        completed.CompleteVisit(null, DateTime.UtcNow.AddMinutes(3));
        var cancelled = CreateBooking(family.Id, owner, elderly.Id, caregiversByUser[4].Id);
        cancelled.CancelByFamily("test cancellation", DateTime.UtcNow.AddMinutes(2));
        var pending = CreatePendingBooking(family.Id, owner, elderly.Id, caregiversByUser[5].Id);
        families.Bookings.AddRange(confirmed, inProgress, disabledPreferenceActive, completed, cancelled, pending);
        await families.SaveChangesAsync();
        await caregivers.SaveChangesAsync();

        var sender = new HelpRequestRecipientSender(
            [owner, confirmedCaregiverUser, inProgressCaregiverUser, admin],
            [admin, owner]);
        var gateway = new Sanad.API.NotificationsIntegration.FamilyNotificationRecipientGateway(families, sender, caregivers);
        var recipients = await InvokeHelpRequest(gateway, elderlyIdentity, elderly.Id.Value);

        Assert.Equal(4, recipients.Count);
        Assert.Contains(owner, recipients);
        Assert.Contains(confirmedCaregiverUser, recipients);
        Assert.Contains(inProgressCaregiverUser, recipients);
        Assert.Contains(admin, recipients);
        Assert.DoesNotContain(disabledFamilyMember, recipients);
        Assert.DoesNotContain(disabledCaregiverUser, recipients);
        Assert.DoesNotContain(completedCaregiverUser, recipients);
        Assert.DoesNotContain(cancelledCaregiverUser, recipients);
        Assert.DoesNotContain(pendingCaregiverUser, recipients);
        Assert.Equal(1, sender.AdminQueryCount);
    }

    [Fact]
    public async Task HelpRequestGateway_FailsClosedForMissingOrMismatchedIdentityBoundElderlyWithoutAdminFanOut()
    {
        await using var families = CreateDb();
        await using var caregivers = CreateCaregiversDb();
        var owner = UserId.New();
        var family = Family.Create(owner, "identity-bound");
        var identity = UserId.New();
        var elderly = Elderly.Create(owner, identity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("Family"), FullName.Create("Elderly"), Gender.Male,
            new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        families.Families.Add(family);
        families.Elderlies.Add(elderly);
        await families.SaveChangesAsync();

        var sender = new HelpRequestRecipientSender([owner], [UserId.New()]);
        var gateway = new Sanad.API.NotificationsIntegration.FamilyNotificationRecipientGateway(families, sender, caregivers);

        Assert.Empty(await InvokeHelpRequest(gateway, identity, Guid.NewGuid()));
        Assert.Empty(await InvokeHelpRequest(gateway, UserId.New(), elderly.Id.Value));
        Assert.Equal(0, sender.AdminQueryCount);
        Assert.Equal(0, sender.RecipientQueryCount);
    }

    [Theory]
    [InlineData("preference", 0)]
    [InlineData("admin", 1)]
    public async Task HelpRequestGateway_FailsClosedWhenAnIdentityRecipientLookupFails(string failurePoint, int expectedAdminQueries)
    {
        await using var families = CreateDb();
        await using var caregivers = CreateCaregiversDb();
        var owner = UserId.New();
        var family = Family.Create(owner, "failed-identity-query");
        var identity = UserId.New();
        var elderly = Elderly.Create(owner, identity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("Family"), FullName.Create("Elderly"), Gender.Male,
            new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        families.Families.Add(family);
        families.Elderlies.Add(elderly);
        await families.SaveChangesAsync();

        var sender = new HelpRequestRecipientSender([owner], [UserId.New()], failurePoint);
        var gateway = new Sanad.API.NotificationsIntegration.FamilyNotificationRecipientGateway(families, sender, caregivers);

        Assert.Empty(await InvokeHelpRequest(gateway, identity, elderly.Id.Value));
        Assert.Equal(expectedAdminQueries, sender.AdminQueryCount);
    }

    private static async Task<IReadOnlyList<UserId>> Invoke(object gateway, UserId elderlyIdentity, Guid elderlyId)
    {
        var recipientType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("Sanad.Modules.Notifications.Application.Abstractions.Recipients.ElderlyRecipient")).First(type => type is not null)!;
        var recipient = Activator.CreateInstance(recipientType, elderlyIdentity, elderlyId)!;
        var task = (Task)gateway.GetType().GetMethod("GetCheckInAlertRecipientsAsync")!.Invoke(gateway, [recipient, CancellationToken.None])!;
        await task;
        return (IReadOnlyList<UserId>)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static FamiliesDbContext CreateDb() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static CaregiversDbContext CreateCaregiversDb() => new(new DbContextOptionsBuilder<CaregiversDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static Booking CreateBooking(FamilyId familyId, UserId owner, ElderlyId elderlyId, CaregiverId caregiverId)
    {
        var booking = CreatePendingBooking(familyId, owner, elderlyId, caregiverId);
        var now = DateTime.UtcNow;
        booking.MarkAsPaid("order-" + Guid.NewGuid(), "transaction", now);
        booking.AcceptByCaregiver(now.AddMinutes(1));
        return booking;
    }

    private static Booking CreatePendingBooking(FamilyId familyId, UserId owner, ElderlyId elderlyId, CaregiverId caregiverId)
    {
        var now = DateTime.UtcNow;
        return Booking.Create(familyId, owner, elderlyId, caregiverId, BookingCaregiverType.Companion,
            BookingShiftType.Hourly, DateOnly.FromDateTime(now), new TimeOnly(8, 0), new TimeOnly(10, 0),
            "address", null, BookingPriceSnapshot.Calculate(100, 10), now.AddHours(1), DateOnly.FromDateTime(now), now);
    }

    private static async Task<IReadOnlyList<UserId>> InvokeMedication(object gateway, UserId elderlyIdentity, Guid elderlyId)
    {
        var recipientType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("Sanad.Modules.Notifications.Application.Abstractions.Recipients.ElderlyRecipient")).First(type => type is not null)!;
        var recipient = Activator.CreateInstance(recipientType, elderlyIdentity, elderlyId)!;
        var task = (Task)gateway.GetType().GetMethod("GetMedicationAlertRecipientsAsync")!.Invoke(gateway, [recipient, CancellationToken.None])!;
        await task;
        return (IReadOnlyList<UserId>)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static async Task<IReadOnlyList<UserId>> InvokeHelpRequest(object gateway, UserId elderlyIdentity, Guid elderlyId)
    {
        var recipientType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("Sanad.Modules.Notifications.Application.Abstractions.Recipients.ElderlyRecipient")).First(type => type is not null)!;
        var recipient = Activator.CreateInstance(recipientType, elderlyIdentity, elderlyId)!;
        var task = (Task)gateway.GetType().GetMethod("GetHelpRequestAlertRecipientsAsync")!.Invoke(gateway, [recipient, CancellationToken.None])!;
        await task;
        return (IReadOnlyList<UserId>)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private sealed class RecipientSender : ISender
    {
        public IReadOnlyList<UserId>? LastIds { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            var ids = (IReadOnlyCollection<UserId>)request.GetType().GetProperty("UserIds")!.GetValue(request)!;
            LastIds = ids.ToArray();
            var values = LastIds.Concat(LastIds.Take(1)).ToArray();
            var resultType = typeof(Result<>).MakeGenericType(typeof(IReadOnlyList<UserId>));
            var result = resultType.GetMethod("Success", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [values]);
            return Task.FromResult((TResponse)result!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class MedicationRecipientSender(UserId admin) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetActiveSupportAdminUserIdsQuery)
            {
                var resultType = typeof(Result<>).MakeGenericType(typeof(IReadOnlyList<UserId>));
                var result = resultType.GetMethod("Success", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [new[] { admin }]);
                return Task.FromResult((TResponse)result!);
            }

            var ids = (IReadOnlyCollection<UserId>)request.GetType().GetProperty("UserIds")!.GetValue(request)!;
            var successType = typeof(Result<>).MakeGenericType(typeof(IReadOnlyList<UserId>));
            var success = successType.GetMethod("Success", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [ids.ToArray()]);
            return Task.FromResult((TResponse)success!);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class HelpRequestRecipientSender(IReadOnlyCollection<UserId> enabledUsers, IReadOnlyCollection<UserId> admins, string? failurePoint = null) : ISender
    {
        public int AdminQueryCount { get; private set; }
        public int RecipientQueryCount { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetActiveSupportAdminUserIdsQuery)
            {
                AdminQueryCount++;
                if (failurePoint == "admin") return Task.FromResult(Failure<TResponse>());
                return Task.FromResult(Success<TResponse>(admins.Concat(admins.Take(1)).ToArray()));
            }

            RecipientQueryCount++;
            if (failurePoint == "preference") return Task.FromResult(Failure<TResponse>());
            var requestedUsers = (IReadOnlyCollection<UserId>)request.GetType().GetProperty("UserIds")!.GetValue(request)!;
            var activeEnabledUsers = enabledUsers.Intersect(requestedUsers).ToArray();
            return Task.FromResult(Success<TResponse>(activeEnabledUsers.Concat(activeEnabledUsers.Take(1)).ToArray()));
        }

        private static TResponse Success<TResponse>(IReadOnlyList<UserId> userIds)
        {
            var resultType = typeof(Result<>).MakeGenericType(typeof(IReadOnlyList<UserId>));
            var result = resultType.GetMethod("Success", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [userIds]);
            return (TResponse)result!;
        }

        private static TResponse Failure<TResponse>()
        {
            var resultType = typeof(Result<>).MakeGenericType(typeof(IReadOnlyList<UserId>));
            var result = resultType.GetMethod("Failure", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [new Error("Identity.Unavailable", "The Identity lookup failed.")]);
            return (TResponse)result!;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
