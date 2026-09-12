using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Invitations;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

public sealed class FamilyDeletionTests
{
    private sealed class FakeFamilyIdentityGateway : IFamilyIdentityGateway
    {
        internal List<IReadOnlyCollection<UserId>> ReceivedUserIds { get; } = [];

        internal Result? FailureToReturn { get; set; }

        public Task<Result<ElderlyIdentityAccount>> GetElderlyByPhoneAsync(
            string phoneNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ElderlyIdentityAccount>.Failure(
                new Error("Test.NotImplemented", "not implemented")));

        public Task<Result<ElderlyIdentityAccount>> CreateElderlyAsync(
            string arabicFullName,
            string englishFullName,
            string phoneNumber,
            Gender gender,
            DateOnly dateOfBirth,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<ElderlyIdentityAccount>.Failure(
                new Error("Test.NotImplemented", "not implemented")));

        public Task DeleteElderlyAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Result<FamilyInviteeAccount>> GetFamilyInviteeByEmailAsync(
            string email,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<FamilyInviteeAccount>.Failure(
                new Error("Test.NotImplemented", "not implemented")));

        public Task<IReadOnlyList<FamilyMemberProfile>> GetFamilyMemberProfilesAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<FamilyMemberProfile>>([]);

        public Task SendFamilyInvitationEmailAsync(
            string email,
            string familyName,
            string invitationToken,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Result> AnonymizeFamilyAccountsAsync(
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken = default)
        {
            ReceivedUserIds.Add(userIds);

            if (FailureToReturn is not null)
            {
                return Task.FromResult(FailureToReturn);
            }

            return Task.FromResult(Result.Success());
        }
    }

    private static FamiliesDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new FamiliesDbContext(options);
    }

    // ------------------------------ Domain ------------------------------

    [Fact]
    public void MarkDeleted_ScrubsName_AndSetsDeletedFields()
    {
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Al-Mansour Family");

        DateTime before = family.UpdatedOnUtc;
        family.MarkDeleted("Reason text", "Optional message");

        Assert.Equal("Deleted family", family.Name);
        Assert.NotNull(family.DeletedOnUtc);
        Assert.Equal("Reason text", family.DeletionReason);
        Assert.Equal("Optional message", family.DeletionMessage);
        Assert.True(family.UpdatedOnUtc >= before);
    }

    [Fact]
    public void MarkDeleted_Throws_OnSecondCall()
    {
        UserId owner = UserId.New();
        Family family = Family.Create(owner);

        family.MarkDeleted("First", "First msg");

        Assert.Throws<DomainException>(
            () => family.MarkDeleted("Second", "Second msg"));
    }

    // ----------------------------- Handler ------------------------------

    [Fact]
    public async Task DeleteFamily_ReturnsNotOwner_WhenCallerIsEditor()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        Family family = Family.Create(owner, "Test Family");
        family.AddMember(FamilyMember.Create(editor, owner, FamilyRelationshipType.Other, FamilyRole.Editor));

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var fake = new FakeFamilyIdentityGateway();
        var handler = new DeleteFamilyCommandHandler(db, fake);

        Result result = await handler.Handle(
            new DeleteFamilyCommand(editor, "reason", "message", true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.NotOwner, result.Error);
    }

    [Fact]
    public async Task DeleteFamily_ReturnsAcknowledgementRequired_WhenNotAcknowledged()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Test Family");

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var fake = new FakeFamilyIdentityGateway();
        var handler = new DeleteFamilyCommandHandler(db, fake);

        Result result = await handler.Handle(
            new DeleteFamilyCommand(owner, "reason", "message", false),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.AcknowledgementRequired, result.Error);
    }

    [Fact]
    public async Task DeleteFamily_ReturnsActiveBookingExists_WhenABookingIsConfirmed()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Test Family");

        Elderly elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("جد"),
            FullName.Create("Grandfather"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        BookingPriceSnapshot price = BookingPriceSnapshot.Calculate(500m, 15.00m);
        Booking booking = Booking.Create(
            family.Id,
            owner,
            elderly.Id,
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            price,
            DateTime.UtcNow.AddHours(24),
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTime.UtcNow);

        booking.MarkAsPaid("order_1", "txn_1", DateTime.UtcNow);
        booking.AcceptByCaregiver(DateTime.UtcNow);

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var fake = new FakeFamilyIdentityGateway();
        var handler = new DeleteFamilyCommandHandler(db, fake);

        Result result = await handler.Handle(
            new DeleteFamilyCommand(owner, "reason", "message", true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.ActiveBookingExists, result.Error);
    }

    [Fact]
    public async Task DeleteFamily_ReturnsUnsettledPaymentExists_WhenAPaymentIsPending()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Test Family");

        Elderly elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("جد"),
            FullName.Create("Grandfather"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        BookingPriceSnapshot price = BookingPriceSnapshot.Calculate(500m, 15.00m);
        Booking booking = Booking.Create(
            family.Id,
            owner,
            elderly.Id,
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            price,
            DateTime.UtcNow.AddHours(24),
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTime.UtcNow);

        // Add a pending payment transaction, then expire the booking
        // so it's not in an active status (PendingPayment, PendingCaregiverApproval, Confirmed, InProgress)
        booking.RecordPaymentIntent("order_pending", PaymentMethod.Card, DateTime.UtcNow);
        booking.Expire(DateTime.UtcNow.AddHours(25)); // Expire after deadline

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var fake = new FakeFamilyIdentityGateway();
        var handler = new DeleteFamilyCommandHandler(db, fake);

        Result result = await handler.Handle(
            new DeleteFamilyCommand(owner, "reason", "message", true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(FamilyErrors.UnsettledPaymentExists, result.Error);
    }

    [Fact]
    public async Task DeleteFamily_AnonymizesDependents_AndRevokesPendingInvitations()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Test Family");

        Elderly elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("جد"),
            FullName.Create("Grandfather"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            "photo_key",
            "Detailed address",
            "Health notes");

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        // Add a pending invitation
        var (invitation, _) = FamilyInvitation.Create(
            family.Id,
            Email.Create("invitee@example.com"),
            UserId.New(),
            FamilyRole.Editor,
            FamilyRelationshipType.Sister,
            owner,
            DateTime.UtcNow);

        // Add an accepted invitation (should not be touched)
        var (acceptedInvitation, _) = FamilyInvitation.Create(
            family.Id,
            Email.Create("accepted@example.com"),
            UserId.New(),
            FamilyRole.Editor,
            FamilyRelationshipType.Sister,
            owner,
            DateTime.UtcNow);

        acceptedInvitation.Accept(acceptedInvitation.InvitedUserId, DateTime.UtcNow);

        db.Invitations.Add(invitation);
        db.Invitations.Add(acceptedInvitation);
        await db.SaveChangesAsync();

        var fake = new FakeFamilyIdentityGateway();
        var handler = new DeleteFamilyCommandHandler(db, fake);

        Result result = await handler.Handle(
            new DeleteFamilyCommand(owner, "manual smoke", "keep this as the last request in the folder", true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Verify elderly anonymized
        Elderly persistedElderly = await db.Elderlies.SingleAsync(e => e.Id == elderly.Id);
        Assert.Equal("محذوف", persistedElderly.ArabicFullName.Value);
        Assert.Equal("Deleted", persistedElderly.EnglishFullName.Value);
        Assert.Null(persistedElderly.ProfileImageKey);
        Assert.Null(persistedElderly.DetailedAddress);
        Assert.Equal("Health notes", persistedElderly.HealthNotes); // Clinical content retained

        // Verify pending invitation revoked
        FamilyInvitation persistedPending = await db.Invitations.SingleAsync(i => i.Id == invitation.Id);
        Assert.Equal(FamilyInvitationStatus.Revoked, persistedPending.Status);

        // Verify accepted invitation untouched
        FamilyInvitation persistedAccepted = await db.Invitations.SingleAsync(i => i.Id == acceptedInvitation.Id);
        Assert.Equal(FamilyInvitationStatus.Accepted, persistedAccepted.Status);

        // Verify members unchanged
        Assert.Single(family.Members);
        Assert.Equal(owner, family.Members.Single().Id);
    }

    [Fact]
    public async Task DeleteFamily_MakesTheFamilyUnresolvable()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Test Family");

        db.Families.Add(family);
        await db.SaveChangesAsync();

        var fake = new FakeFamilyIdentityGateway();
        var handler = new DeleteFamilyCommandHandler(db, fake);

        Result result1 = await handler.Handle(
            new DeleteFamilyCommand(owner, "reason", "message", true),
            CancellationToken.None);

        Assert.True(result1.IsSuccess);

        // Second call should return NotFound because family is deleted
        Result result2 = await handler.Handle(
            new DeleteFamilyCommand(owner, "reason", "message", true),
            CancellationToken.None);

        Assert.True(result2.IsFailure);
        Assert.Equal(FamilyErrors.NotFound, result2.Error);
    }

    [Fact]
    public async Task DeleteFamily_BlocksNothing_WhenTheOnlyBookingIsClosed()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Test Family");

        Elderly elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("جد"),
            FullName.Create("Grandfather"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        BookingPriceSnapshot price = BookingPriceSnapshot.Calculate(500m, 15.00m);
        Booking booking = Booking.Create(
            family.Id,
            owner,
            elderly.Id,
            CaregiverId.New(),
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            new TimeOnly(10, 0),
            new TimeOnly(12, 0),
            "123 Nile St, Cairo",
            null,
            price,
            DateTime.UtcNow.AddHours(24),
            DateOnly.FromDateTime(DateTime.UtcNow),
            DateTime.UtcNow);

        booking.MarkAsPaid("order_1", "txn_1", DateTime.UtcNow);
        booking.AcceptByCaregiver(DateTime.UtcNow);
        booking.StartVisit(DateTime.UtcNow);
        booking.CompleteVisit("Done", DateTime.UtcNow);

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var fake = new FakeFamilyIdentityGateway();
        var handler = new DeleteFamilyCommandHandler(db, fake);

        Result result = await handler.Handle(
            new DeleteFamilyCommand(owner, "reason", "message", true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteFamily_AnonymizesIdentityAccounts_WithMemberAndDependentIds()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId editor = UserId.New();
        UserId dependentIdentity = UserId.New();

        Family family = Family.Create(owner, "Test Family");
        family.AddMember(FamilyMember.Create(editor, owner, FamilyRelationshipType.Other, FamilyRole.Editor));

        Elderly elderly = Elderly.Create(
            owner,
            dependentIdentity,
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("جد"),
            FullName.Create("Grandfather"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        var fake = new FakeFamilyIdentityGateway();
        var handler = new DeleteFamilyCommandHandler(db, fake);

        Result result = await handler.Handle(
            new DeleteFamilyCommand(owner, "reason", "message", true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Single(fake.ReceivedUserIds);
        IReadOnlyCollection<UserId> received = fake.ReceivedUserIds[0];

        Assert.Equal(3, received.Count);
        Assert.Contains(owner, received);
        Assert.Contains(editor, received);
        Assert.Contains(dependentIdentity, received);
    }

    [Fact]
    public async Task DeleteFamily_ReturnsIdentityFailure_AndDeletesNothing()
    {
        using FamiliesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Test Family");

        Elderly elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("جد"),
            FullName.Create("Grandfather"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow),
            "photo_key",
            "Detailed address",
            "Health notes");

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        await db.SaveChangesAsync();

        var (invitation, _) = FamilyInvitation.Create(
            family.Id,
            Email.Create("invitee@example.com"),
            UserId.New(),
            FamilyRole.Editor,
            FamilyRelationshipType.Sister,
            owner,
            DateTime.UtcNow);

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        string originalArabic = elderly.ArabicFullName.Value;
        string originalEnglish = elderly.EnglishFullName.Value;

        var fake = new FakeFamilyIdentityGateway
        {
            FailureToReturn = Result.Failure(new Error("Test.IdentityFailure", "boom"))
        };

        var handler = new DeleteFamilyCommandHandler(db, fake);

        Result result = await handler.Handle(
            new DeleteFamilyCommand(owner, "reason", "message", true),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Test.IdentityFailure", result.Error.Code);

        // Assert nothing deleted
        Assert.Null(family.DeletedOnUtc);

        Elderly persistedElderly = await db.Elderlies.SingleAsync(e => e.Id == elderly.Id);
        Assert.Equal(originalArabic, persistedElderly.ArabicFullName.Value);
        Assert.Equal(originalEnglish, persistedElderly.EnglishFullName.Value);

        FamilyInvitation persistedInvitation = await db.Invitations.SingleAsync(i => i.Id == invitation.Id);
        Assert.Equal(FamilyInvitationStatus.Pending, persistedInvitation.Status);
    }
}
