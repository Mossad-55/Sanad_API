using MediatR;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Caregivers.Domain.Caregivers;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;
using Ahlena.Modules.Families.Domain.Bookings;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Families.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Application.Users;
using Ahlena.Modules.Notifications.Application.Abstractions.Recipients;
using Ahlena.Modules.Notifications.Application.Notifications;
using Ahlena.Modules.Notifications.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Notifications;

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
        var recipients = await new Ahlena.API.NotificationsIntegration.FamilyNotificationRecipientGateway(families, new RecipientSender(admin), caregivers).GetSosAlertRecipientsAsync(new ElderlyRecipient(elderly.IdentityUserId, elderly.Id.Value));
        Assert.Equal(new[] { owner, member, assignedUser, inProgressUser, admin }.OrderBy(x => x.Value), recipients.OrderBy(x => x.Value)); Assert.DoesNotContain(pendingUser, recipients); Assert.DoesNotContain(foreign, recipients); Assert.DoesNotContain(foreignElderly.IdentityUserId, recipients);
    }

    [Fact]
    public async Task Handler_PersistsDurableNotifications_AndMakesReplayIdempotent()
    {
        await using var db = new NotificationsDbContext(new DbContextOptionsBuilder<NotificationsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        // F8: one recipient keeps the Arabic default, one persists English.
        var arabicRecipient = UserId.New();
        var englishRecipient = UserId.New();
        var sosId = Guid.NewGuid();
        var languages = FixedNotificationLanguageGateway.For(englishRecipient.Value, UiLanguage.English);
        var handler = new CreateSosAlertNotificationsCommandHandler(db, new FixedRecipients(arabicRecipient, englishRecipient), languages);
        var command = new CreateSosAlertNotificationsCommand(new ElderlyRecipient(UserId.New(), Guid.NewGuid()), sosId, DateTime.UtcNow);
        var first = await handler.Handle(command, default); var replay = await handler.Handle(command, default); Assert.Equal(2, first.Value); Assert.Equal(0, replay.Value); Assert.Equal(2, await db.Notifications.CountAsync()); Assert.Equal(2, await db.Notifications.Select(x => x.IdempotencyKey).Distinct().CountAsync());

        // F8: both recipients are answered by a single batch lookup, and each
        // stored row reads in that recipient's own language with the structured
        // SOS id kept on the destination instead of inside the copy.
        // One batch per invocation: the first call and the idempotent replay
        // each resolve copy once, never once per recipient.
        Assert.Equal(2, languages.LookupCount);
        Assert.Equal(new[] { arabicRecipient.Value, englishRecipient.Value }.OrderBy(x => x), languages.RequestedIds.OrderBy(x => x));
        var rows = await db.Notifications.ToListAsync();
        var arabicRow = Assert.Single(rows, x => x.RecipientUserId == arabicRecipient.Value);
        var englishRow = Assert.Single(rows, x => x.RecipientUserId == englishRecipient.Value);
        Assert.Equal(NotificationTextCatalog.SosAlert(UiLanguage.Arabic).Title, arabicRow.Title);
        Assert.Equal(NotificationTextCatalog.SosAlert(UiLanguage.Arabic).Body, arabicRow.Body);
        Assert.Equal(NotificationTextCatalog.SosAlert(UiLanguage.English).Title, englishRow.Title);
        Assert.Equal(NotificationTextCatalog.SosAlert(UiLanguage.English).Body, englishRow.Body);
        Assert.All(rows, row =>
        {
            Assert.Equal("ElderlySos", row.Category);
            Assert.Equal("SosCreated", row.Type);
            Assert.Equal("ElderlySos", row.DestinationEntityKind);
            Assert.Equal(sosId, row.DestinationEntityId);
            Assert.DoesNotContain(sosId.ToString(), row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sosId.ToString("N"), row.Title + row.Body, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static Booking ActiveBooking(FamilyId f, UserId o, ElderlyId e, CaregiverId c) { var b = PendingBooking(f, o, e, c); var n = DateTime.UtcNow; b.MarkAsPaid(Guid.NewGuid().ToString(), "tx", n); b.AcceptByCaregiver(n.AddMinutes(1)); return b; }
    private static Booking InProgressBooking(FamilyId f, UserId o, ElderlyId e, CaregiverId c) { var b = ActiveBooking(f, o, e, c); b.StartVisit(DateTime.UtcNow.AddMinutes(2)); return b; }
    private static Booking PendingBooking(FamilyId f, UserId o, ElderlyId e, CaregiverId c) => Booking.Create(f, o, e, c, BookingCaregiverType.Companion, BookingShiftType.Hourly, DateOnly.FromDateTime(DateTime.UtcNow), new TimeOnly(8), new TimeOnly(10), "address", null, BookingPriceSnapshot.Calculate(100, 10), DateTime.UtcNow.AddHours(1), DateOnly.FromDateTime(DateTime.UtcNow), DateTime.UtcNow);
    private static FamiliesDbContext CreateFamilies() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options); private static CaregiversDbContext CreateCaregivers() => new(new DbContextOptionsBuilder<CaregiversDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class FixedRecipients(UserId a, UserId b) : INotificationRecipientGateway { public Task<IReadOnlyList<UserId>> GetCheckInAlertRecipientsAsync(ElderlyRecipient e, CancellationToken c = default) => Task.FromResult<IReadOnlyList<UserId>>([]); public Task<IReadOnlyList<UserId>> GetHelpRequestAlertRecipientsAsync(ElderlyRecipient e, CancellationToken c = default) => Task.FromResult<IReadOnlyList<UserId>>([]); public Task<IReadOnlyList<UserId>> GetMedicationAlertRecipientsAsync(ElderlyRecipient e, CancellationToken c = default) => Task.FromResult<IReadOnlyList<UserId>>([]); public Task<IReadOnlyList<UserId>> GetSosAlertRecipientsAsync(ElderlyRecipient e, CancellationToken c = default) => Task.FromResult<IReadOnlyList<UserId>>([a, b]); }
    private sealed class RecipientSender(UserId admin) : ISender { public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken c = default) { if (request is GetActiveSupportAdminUserIdsQuery) return Task.FromResult((TResponse)(object)Result<IReadOnlyList<UserId>>.Success([admin])); var ids = (IReadOnlyCollection<UserId>)request.GetType().GetProperty("UserIds")!.GetValue(request)!; return Task.FromResult((TResponse)(object)Result<IReadOnlyList<UserId>>.Success(ids.ToArray())); } public Task Send<TRequest>(TRequest request, CancellationToken c = default) where TRequest : IRequest => throw new NotSupportedException(); public Task<object?> Send(object request, CancellationToken c = default) => throw new NotSupportedException(); public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> r, CancellationToken c = default) => throw new NotSupportedException(); public IAsyncEnumerable<object?> CreateStream(object r, CancellationToken c = default) => throw new NotSupportedException(); }
}
