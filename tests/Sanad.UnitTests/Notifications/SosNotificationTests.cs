using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Notifications.Application.Notifications;
using Sanad.Modules.Notifications.Infrastructure.Persistence;

namespace Sanad.UnitTests.Notifications;

public sealed class SosNotificationTests
{
    [Fact]
    public async Task Recipients_IncludeLinkedFamilyHelpRequestRecipients_ActiveAssignedCaregiversAndSupportAdmin()
    {
        await using var families = CreateFamilies(); await using var caregivers = CreateCaregivers();
        var owner = UserId.New(); var member = UserId.New(); var foreign = UserId.New(); var admin = UserId.New(); var assignedUser = UserId.New(); var inProgressUser = UserId.New(); var pendingUser = UserId.New();
        var family = Family.Create(owner, "target"); family.AddMember(FamilyMember.Create(member, owner, FamilyRelationshipType.Son, FamilyRole.Editor));
        var foreignFamily = Family.Create(UserId.New(), "foreign"); foreignFamily.AddMember(FamilyMember.Create(foreign, owner, FamilyRelationshipType.Son, FamilyRole.Editor));
        var elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father, FullName.Create("Omar"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        var foreignElderly = Elderly.Create(foreignFamily.OwnerUserId, UserId.New(), foreignFamily.Id, FamilyRelationshipType.Father, FullName.Create("Foreign"), FullName.Create("Foreign"), Gender.Male, new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
        var assigned = Caregiver.Create(assignedUser, CaregiverType.Companion); var inProgress = Caregiver.Create(inProgressUser, CaregiverType.Companion); var pending = Caregiver.Create(pendingUser, CaregiverType.Companion);
        families.Families.AddRange(family, foreignFamily); families.Elderlies.AddRange(elderly, foreignElderly); caregivers.Caregivers.AddRange(assigned, inProgress, pending);
        families.Bookings.AddRange(ActiveBooking(family.Id, owner, elderly.Id, assigned.Id), InProgressBooking(family.Id, owner, elderly.Id, inProgress.Id), PendingBooking(family.Id, owner, elderly.Id, pending.Id)); await families.SaveChangesAsync(); await caregivers.SaveChangesAsync();
        var recipients = await new Sanad.API.NotificationsIntegration.FamilyNotificationRecipientGateway(families, new RecipientSender(admin), caregivers).GetSosAlertRecipientsAsync(new ElderlyRecipient(elderly.IdentityUserId, elderly.Id.Value));
        Assert.Equal(new[] { owner, member, assignedUser, inProgressUser, admin }.OrderBy(x => x.Value), recipients.OrderBy(x => x.Value)); Assert.DoesNotContain(pendingUser, recipients); Assert.DoesNotContain(foreign, recipients); Assert.DoesNotContain(foreignElderly.IdentityUserId, recipients);
    }

    [Fact]
    public async Task Handler_PersistsDurableNotifications_AndMakesReplayIdempotent()
    {
        await using var db = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options); var handler = new CreateSosAlertNotificationsCommandHandler(db, new FixedRecipients(UserId.New(), UserId.New())); var command = new CreateSosAlertNotificationsCommand(new ElderlyRecipient(UserId.New(), Guid.NewGuid()), Guid.NewGuid(), DateTime.UtcNow);
        var first = await handler.Handle(command, default); var replay = await handler.Handle(command, default); Assert.Equal(2, first.Value); Assert.Equal(0, replay.Value); Assert.Equal(2, await db.Notifications.CountAsync()); Assert.Equal(2, await db.Notifications.Select(x => x.IdempotencyKey).Distinct().CountAsync());
    }

    private static Booking ActiveBooking(FamilyId f, UserId o, ElderlyId e, CaregiverId c) { var b = PendingBooking(f, o, e, c); var n = DateTime.UtcNow; b.MarkAsPaid(Guid.NewGuid().ToString(), "tx", n); b.AcceptByCaregiver(n.AddMinutes(1)); return b; }
    private static Booking InProgressBooking(FamilyId f, UserId o, ElderlyId e, CaregiverId c) { var b = ActiveBooking(f, o, e, c); b.StartVisit(DateTime.UtcNow.AddMinutes(2)); return b; }
    private static Booking PendingBooking(FamilyId f, UserId o, ElderlyId e, CaregiverId c) => Booking.Create(f, o, e, c, BookingCaregiverType.Companion, BookingShiftType.Hourly, DateOnly.FromDateTime(DateTime.UtcNow), new TimeOnly(8), new TimeOnly(10), "address", null, BookingPriceSnapshot.Calculate(100, 10), DateTime.UtcNow.AddHours(1), DateOnly.FromDateTime(DateTime.UtcNow), DateTime.UtcNow);
    private static FamiliesDbContext CreateFamilies() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options); private static CaregiversDbContext CreateCaregivers() => new(new DbContextOptionsBuilder<CaregiversDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class FixedRecipients(UserId a, UserId b) : INotificationRecipientGateway { public Task<IReadOnlyList<UserId>> GetCheckInAlertRecipientsAsync(ElderlyRecipient e, CancellationToken c = default) => Task.FromResult<IReadOnlyList<UserId>>([]); public Task<IReadOnlyList<UserId>> GetHelpRequestAlertRecipientsAsync(ElderlyRecipient e, CancellationToken c = default) => Task.FromResult<IReadOnlyList<UserId>>([]); public Task<IReadOnlyList<UserId>> GetMedicationAlertRecipientsAsync(ElderlyRecipient e, CancellationToken c = default) => Task.FromResult<IReadOnlyList<UserId>>([]); public Task<IReadOnlyList<UserId>> GetSosAlertRecipientsAsync(ElderlyRecipient e, CancellationToken c = default) => Task.FromResult<IReadOnlyList<UserId>>([a, b]); }
    private sealed class RecipientSender(UserId admin) : ISender { public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken c = default) { if (request is GetActiveSupportAdminUserIdsQuery) return Task.FromResult((TResponse)(object)Result<IReadOnlyList<UserId>>.Success([admin])); var ids = (IReadOnlyCollection<UserId>)request.GetType().GetProperty("UserIds")!.GetValue(request)!; return Task.FromResult((TResponse)(object)Result<IReadOnlyList<UserId>>.Success(ids.ToArray())); } public Task Send<TRequest>(TRequest request, CancellationToken c = default) where TRequest : IRequest => throw new NotSupportedException(); public Task<object?> Send(object request, CancellationToken c = default) => throw new NotSupportedException(); public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> r, CancellationToken c = default) => throw new NotSupportedException(); public IAsyncEnumerable<object?> CreateStream(object r, CancellationToken c = default) => throw new NotSupportedException(); }
}
